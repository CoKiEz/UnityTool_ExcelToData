using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ExcelDataTool.Editor.UI
{
    /// <summary>保存当前工程的导表来源、输出选项和工作表选择，不包含表格数据。</summary>
    [Serializable]
    public sealed class ExcelDataToolWindowSettings
    {
        /// <summary>用于识别本工具设置文件的固定标记。</summary>
        public string Format = "ExcelDataTool.Settings.v1";
        /// <summary>文件或递归源目录，可使用工程相对路径或绝对路径。</summary>
        public List<string> SourcePaths = new List<string>();
        /// <summary>工程内的 JSON 输出目录。</summary>
        public string JsonDirectory = "Assets/GameData/Generated/Json";
        /// <summary>工程内供运行时使用的 C# 输出目录。</summary>
        public string CodeDirectory = "Assets/GameData/Generated/Code";
        /// <summary>生成的行类型和表容器使用的命名空间。</summary>
        public string GeneratedNamespace = "ExcelDataTool.Generated";
        /// <summary>当前选择表的源工作簿绝对路径。</summary>
        public string SelectedSourcePath = string.Empty;
        /// <summary>当前选择的工作表名称。</summary>
        public string SelectedSheetName = string.Empty;
    }

    /// <summary>在工程 UserSettings 内保存工具专属 JSON，并提供可隔离测试的显式路径。</summary>
    public static class ExcelDataToolSettingsStore
    {
        /// <summary>读取设置；文件不存在时仅返回默认值，不创建文件。</summary>
        /// <param name="projectRoot">允许读取设置的工程根目录。</param>
        /// <param name="settingsPath">可选的工程内设置文件路径；默认 UserSettings/ExcelDataTool.json。</param>
        /// <returns>已初始化非空集合和默认文本的窗口设置。</returns>
        /// <exception cref="IOException">文件无法读取、格式错误或不是本工具的设置文件。</exception>
        /// <exception cref="ArgumentException">设置路径越界或工程根目录无效。</exception>
        public static ExcelDataToolWindowSettings Load(string projectRoot, string settingsPath = null)
        {
            string path = ResolvePath(projectRoot, settingsPath);
            if (!File.Exists(path)) return new ExcelDataToolWindowSettings();
            try
            {
                JObject document = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (document["Format"]?.Type != JTokenType.String || (string)document["Format"] != "ExcelDataTool.Settings.v1")
                    throw new IOException("设置文件缺少 ExcelDataTool 标记，请先备份并检查：" + path);
                ExcelDataToolWindowSettings settings = document.ToObject<ExcelDataToolWindowSettings>();
                settings.SourcePaths = settings.SourcePaths ?? new List<string>();
                settings.SourcePaths.RemoveAll(string.IsNullOrWhiteSpace);
                settings.JsonDirectory = settings.JsonDirectory ?? "Assets/GameData/Generated/Json";
                settings.CodeDirectory = settings.CodeDirectory ?? "Assets/GameData/Generated/Code";
                settings.GeneratedNamespace = settings.GeneratedNamespace ?? "ExcelDataTool.Generated";
                settings.SelectedSourcePath = settings.SelectedSourcePath ?? string.Empty;
                settings.SelectedSheetName = settings.SelectedSheetName ?? string.Empty;
                return settings;
            }
            catch (JsonException exception)
            {
                throw new IOException("无法解析导表设置，请先备份并检查：" + path, exception);
            }
        }

        /// <summary>保存工具设置，先写临时文件再替换，拒绝覆盖格式未知的已有文件。</summary>
        /// <param name="settings">需要保存的窗口设置。</param>
        /// <param name="projectRoot">允许写入设置的工程根目录。</param>
        /// <param name="settingsPath">可选的工程内设置文件路径，供独立配置或测试使用。</param>
        /// <exception cref="ArgumentNullException">设置对象为空。</exception>
        /// <exception cref="ArgumentException">设置路径越界、路径格式无效或存在重解析点。</exception>
        /// <exception cref="IOException">现有文件归属未知，或写入和替换失败。</exception>
        public static void Save(ExcelDataToolWindowSettings settings, string projectRoot, string settingsPath = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            string path = ResolvePath(projectRoot, settingsPath);
            byte[] previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (previous != null) Load(projectRoot, path);
            settings.Format = "ExcelDataTool.Settings.v1";
            string contents = JsonConvert.SerializeObject(settings, Formatting.Indented) + "\n";
            if (File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == contents) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, contents, new UTF8Encoding(false));
                byte[] latest = File.Exists(path) ? File.ReadAllBytes(path) : null;
                if ((previous == null) != (latest == null) || (previous != null && !System.Linq.Enumerable.SequenceEqual(previous, latest)))
                    throw new IOException("设置文件在保存期间已被其他操作修改，本次保存已停止。");
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        /// <summary>限定设置文件位于工程内部，并拒绝符号链接等可能绕过边界的路径。</summary>
        private static string ResolvePath(string projectRoot, string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("工程根目录不能为空。", nameof(projectRoot));
            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string requested = settingsPath ?? Path.Combine("UserSettings", "ExcelDataTool.json");
            string path = Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(root, requested));
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                throw new ArgumentException("设置文件必须位于当前工程内。", nameof(settingsPath));
            string cursor = path;
            while (!string.IsNullOrEmpty(cursor) && cursor.Length >= root.Length)
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("设置路径不能经过符号链接或目录联接。", nameof(settingsPath));
                cursor = Path.GetDirectoryName(cursor);
            }
            return path;
        }
    }
}
