using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace ExcelDataTool.Editor.Distribution
{
    /// <summary>按固定文件清单导出工具与模板发行包，避免带入业务生成物、测试、工程配置及隐式依赖。</summary>
    /// <remarks>本脚本仅供源码工程制作发行物使用，不包含在工具发行包中。</remarks>
    public static class ExcelDataToolPackageExporter
    {
        /// <summary>当前发行版本；升级此常量会同时更新默认输出目录和两个包的文件名。</summary>
        public const string Version = "1.0.0";

        // 文件清单必须明确维护；不能用递归目录或依赖收集替代，否则可能把用户项目内容带入分发包。
        private static readonly string[] ToolAssets =
        {
            "Assets/ExcelDataTool/Editor/ExcelDataTool.Editor.asmdef",
            "Assets/ExcelDataTool/Editor/Core/BatchExportService.cs",
            "Assets/ExcelDataTool/Editor/Core/TableExportResult.cs",
            "Assets/ExcelDataTool/Editor/Core/TableExporter.cs",
            "Assets/ExcelDataTool/Editor/Core/TableValidator.cs",
            "Assets/ExcelDataTool/Editor/Core/WorkbookModels.cs",
            "Assets/ExcelDataTool/Editor/Core/WorkbookScanner.cs",
            "Assets/ExcelDataTool/Editor/Core/XlsxWorkbookReader.cs",
            "Assets/ExcelDataTool/Editor/UI/ExcelDataToolWindow.cs",
            "Assets/ExcelDataTool/Editor/UI/ExcelDataToolWindowSettings.cs",
            "Assets/ExcelDataTool/Editor/UI/TablePreviewData.cs",
            "Assets/ExcelDataTool/Editor/UI/TablePreviewView.cs",
            "Assets/ExcelDataTool/Runtime/ExcelDataTool.Runtime.asmdef",
            "Assets/ExcelDataTool/Runtime/JsonTableLoader.cs",
            "Assets/ExcelDataTool/Documentation/README.md",
            "Assets/ExcelDataTool/Documentation/LICENSE.txt",
            "Assets/ExcelDataTool/Documentation/ThirdPartyNotices.md"
        };

        private static readonly string[] TemplateAssets =
        {
            "Assets/GameData/Excel/Enemy.xlsx",
            "Assets/GameData/Excel/Card.xlsx",
            "Assets/ExcelDataTool/Documentation/LICENSE.txt"
        };

        /// <summary>供 Unity 命令行通过 -executeMethod 调用，在默认版本目录导出两个发行包。</summary>
        /// <remarks>此入口无参数或返回值，不主动退出编辑器；批处理命令应同时传入 -quit。失败记录日志并重新抛出异常。</remarks>
        /// <exception cref="IOException">发行源不完整、已有同名目标、导出失败或安全检查未通过。</exception>
        /// <exception cref="UnauthorizedAccessException">缺少读取源文件或写入 Releases 目录的权限。</exception>
        public static void ExportBatch()
        {
            try { ExportReleasePackages(); }
            catch (Exception exception)
            {
                Debug.LogError("Excel Data Tool 发行包导出失败：" + exception.Message);
                throw;
            }
        }

        /// <summary>预检全部源文件和目标路径，再导出两个临时包，最后以不覆盖已有文件的方式发布。</summary>
        /// <param name="outputDirectory">当前工程 Releases 内的相对或绝对目录，默认使用当前版本目录。</param>
        /// <returns>工具包、模板包的两个绝对路径；仅在两个包均发布成功后返回。</returns>
        /// <exception cref="ArgumentException">输出目录为空、包含穿越路径或位于工程 Releases 之外。</exception>
        /// <exception cref="IOException">输入文件或元数据缺失、目标已存在、遇到链接路径，或导出与发布失败。</exception>
        /// <exception cref="UnauthorizedAccessException">文件或目录缺少访问权限。</exception>
        public static IReadOnlyList<string> ExportReleasePackages(string outputDirectory = "Releases/" + Version)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string destination = ResolveDestination(root, outputDirectory);
            var packages = new[]
            {
                new PendingPackage(ToolAssets, Path.Combine(destination, "ExcelDataTool-" + Version + ".unitypackage")),
                new PendingPackage(TemplateAssets, Path.Combine(destination, "ExcelDataTool-Templates-" + Version + ".unitypackage"))
            };

            // 两包一起预检：缺少文档或第二个目标已存在时，第一个包也不能开始导出。
            ValidateInputs(root, ToolAssets.Concat(TemplateAssets).Distinct(StringComparer.Ordinal));
            foreach (PendingPackage package in packages) EnsureTargetAbsent(root, package.Target);
            Directory.CreateDirectory(destination);

            try
            {
                foreach (PendingPackage package in packages)
                {
                    package.Temporary = Path.Combine(destination, "." + Guid.NewGuid().ToString("N") + ".unitypackage");
                    EnsureTargetAbsent(root, package.Temporary);
                    // 先独占创建占位文件，保证后续清理只涉及本次持有的临时路径。
                    using (File.Open(package.Temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    package.OwnsTemporary = true;
                    AssetDatabase.ExportPackage(package.Assets, package.Temporary, ExportPackageOptions.Default);
                    EnsureNoReparsePoints(root, package.Temporary);
                    if (!File.Exists(package.Temporary) || new FileInfo(package.Temporary).Length == 0)
                        throw new IOException("Unity 没有生成有效的临时发行包：" + package.Temporary);
                    package.Hash = ComputeHash(package.Temporary);
                }

                foreach (PendingPackage package in packages) EnsureTargetAbsent(root, package.Target);
                foreach (PendingPackage package in packages)
                {
                    EnsureTargetAbsent(root, package.Target);
                    File.Move(package.Temporary, package.Target);
                    package.OwnsTemporary = false;
                    package.Published = true;
                }

                string[] paths = packages.Select(package => package.Target).ToArray();
                Debug.Log("Excel Data Tool " + Version + " 发行包导出成功。工具包包含 " + ToolAssets.Length +
                    " 个明确列出的文件；模板包包含两个工作簿及 MIT 许可证。未打入隐式依赖。\n" + string.Join("\n", paths));
                return Array.AsReadOnly(paths);
            }
            catch (Exception exportFailure)
            {
                var rollbackFailures = new List<string>();
                foreach (PendingPackage package in packages.Reverse().Where(item => item.Published))
                {
                    try
                    {
                        EnsureNoReparsePoints(root, package.Target);
                        if (!File.Exists(package.Target) || ComputeHash(package.Target) != package.Hash)
                            throw new IOException("本次发布后文件再次变化，已保留现场，不能自动撤回。");
                        // 只撤回本次创建且摘要仍一致的包；File.Move 本身不会覆盖任何既有临时文件。
                        EnsureTargetAbsent(root, package.Temporary);
                        File.Move(package.Target, package.Temporary);
                        package.OwnsTemporary = true;
                        package.Published = false;
                    }
                    catch (Exception rollbackFailure)
                    {
                        rollbackFailures.Add(package.Target + "；" + rollbackFailure.Message);
                    }
                }
                if (rollbackFailures.Count > 0)
                    throw new IOException("发行包导出失败，以下本次产物需要人工检查：\n" + string.Join("\n", rollbackFailures), exportFailure);
                throw;
            }
            finally
            {
                foreach (PendingPackage package in packages)
                    if (package.OwnsTemporary) DeleteOwnTemporary(root, package.Temporary);
            }
        }

        /// <summary>提供与现有导表窗口菜单互不冲突的发行操作，普通错误只记录清晰日志。</summary>
        [MenuItem("Tools/Export Excel Data Tool Packages")]
        private static void ExportFromMenu()
        {
            try { ExportReleasePackages(); }
            catch (Exception exception) { Debug.LogError("Excel Data Tool 发行包导出失败：" + exception.Message); }
        }

        /// <summary>检查每个白名单资产、元数据和已导入 GUID；不自动导入依赖或修复未知文件。</summary>
        private static void ValidateInputs(string root, IEnumerable<string> assetPaths)
        {
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string assetPath in assetPaths)
            {
                string fullPath = Path.GetFullPath(Path.Combine(root, assetPath));
                EnsureNoReparsePoints(root, fullPath);
                EnsureNoReparsePoints(root, fullPath + ".meta");
                if (!File.Exists(fullPath) || !File.Exists(fullPath + ".meta"))
                    throw new IOException("发行清单中的源文件或 .meta 缺失，请先准备并完成 Unity 导入：" + assetPath);
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid) || !guids.Add(guid))
                    throw new IOException("发行资产尚未导入或 GUID 重复，请先检查 Console 与元数据：" + assetPath);
            }
        }

        /// <summary>将发行目录限制在当前工程 Releases 内，并拒绝路径穿越和链接绕过。</summary>
        private static string ResolveDestination(string root, string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("发行输出目录不能为空。", nameof(directory));
            string normalized = directory.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (normalized.Split(Path.DirectorySeparatorChar).Any(part => part == "." || part == ".."))
                throw new ArgumentException("发行输出目录不能包含 . 或 .. 路径段。", nameof(directory));
            string fullPath = Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(root, normalized))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!IsWithin(Path.Combine(root, "Releases"), fullPath))
                throw new ArgumentException("发行包只能写入当前工程的 Releases 目录。", nameof(directory));
            EnsureNoReparsePoints(root, fullPath);
            if (File.Exists(fullPath)) throw new IOException("发行目录位置已有同名文件：" + fullPath);
            return fullPath;
        }

        /// <summary>只允许创建新包；同名既有文件或目录一律保留，不区分是否为本工具的旧发行物。</summary>
        private static void EnsureTargetAbsent(string root, string path)
        {
            EnsureNoReparsePoints(root, path);
            if (File.Exists(path) || Directory.Exists(path))
                throw new IOException("目标已存在，已阻止覆盖。请使用新的发行目录或先自行处理旧文件：" + path);
        }

        /// <summary>按当前操作系统的路径大小写规则检查真正的父子目录边界。</summary>
        private static bool IsWithin(string parent, string path)
        {
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(parent, path, comparison) || path.StartsWith(parent + Path.DirectorySeparatorChar, comparison);
        }

        /// <summary>检查从工程根到目标的所有已有路径段，避免符号链接或目录联接把读写重定向到其他位置。</summary>
        private static void EnsureNoReparsePoints(string root, string path)
        {
            if (!IsWithin(root, path)) throw new IOException("发行路径越出当前工程：" + path);
            string current = path;
            while (true)
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("发行路径不允许包含符号链接或目录联接：" + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (string.Equals(current, root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) break;
                current = Path.GetDirectoryName(current);
            }
        }

        /// <summary>计算本次创建包的摘要，确保错误恢复不会撤回导出期间被外部改动的文件。</summary>
        private static string ComputeHash(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(stream));
        }

        /// <summary>只清理本次明确创建的临时包；清理失败不掩盖导出结果，也不删除任何目录。</summary>
        private static void DeleteOwnTemporary(string root, string path)
        {
            if (path == null) return;
            try
            {
                EnsureNoReparsePoints(root, path);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("发行临时包未能清理，请检查：" + path + "；" + exception.Message);
            }
        }

        /// <summary>记录单个包的明确输入、临时路径及发布状态，仅用于本次发行事务。</summary>
        private sealed class PendingPackage
        {
            internal readonly string[] Assets;
            internal readonly string Target;
            internal string Temporary;
            internal string Hash;
            internal bool Published;
            internal bool OwnsTemporary;

            /// <summary>固定本次包内容与目标，不收集任何未列入清单的依赖。</summary>
            internal PendingPackage(string[] assets, string target)
            {
                Assets = assets;
                Target = target;
            }
        }
    }
}
