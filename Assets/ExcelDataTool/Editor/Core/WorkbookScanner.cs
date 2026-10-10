using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExcelDataTool.Editor.Core
{
    /// <summary>保存一张工作表的读取、校验及全局命名检查结果，供预览和导出共用。</summary>
    public sealed class ScannedTable
    {
        /// <summary>源文件完整路径；无法规范化的输入使用可安全显示的路径说明。</summary>
        public string SourcePath { get; }

        /// <summary>实际工作表名；工作簿无法读取时为“（工作簿）”。</summary>
        public string Name { get; }

        /// <summary>可供预览的原始工作表；工作簿读取失败时为 null。</summary>
        public RawSheet Sheet { get; }

        /// <summary>可导出的强类型中间数据；任何校验或表名冲突失败时为 null。</summary>
        public ValidatedTable Table { get; }

        /// <summary>包含文件、工作表和单元格位置的全部错误。</summary>
        public IReadOnlyList<ValidationIssue> Issues { get; }

        /// <summary>是否通过单表校验和整个扫描范围内的表名唯一性检查。</summary>
        public bool IsValid => Table != null && Issues.Count == 0;

        /// <summary>复制错误列表，使扫描结果不受调用方后续修改影响。</summary>
        internal ScannedTable(string sourcePath, string name, RawSheet sheet, ValidatedTable table, IEnumerable<ValidationIssue> issues)
        {
            SourcePath = sourcePath;
            Name = name;
            Sheet = sheet;
            Table = table;
            Issues = new List<ValidationIssue>(issues).AsReadOnly();
        }
    }

    /// <summary>保存一次完整扫描的工作表、源文件清单和不阻断导出的提示。</summary>
    public sealed class WorkbookScanResult
    {
        /// <summary>按源路径稳定排序的工作表及读取失败占位条目。</summary>
        public IReadOnlyList<ScannedTable> Tables { get; }

        /// <summary>被跳过的临时锁文件、重解析目录等提示。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>本次实际发现并尝试读取的去重 .xlsx 完整路径。</summary>
        public IReadOnlyList<string> SourceFiles { get; }

        /// <summary>已成功读取工作簿结构的完整路径，不要求其中所有表均通过数据校验。</summary>
        public IReadOnlyList<string> SuccessfullyReadSourcePaths { get; }

        /// <summary>固定本次扫描快照，避免导出时依赖界面列表的可变状态。</summary>
        internal WorkbookScanResult(List<ScannedTable> tables, List<string> warnings, List<string> sourceFiles, List<string> successfulPaths)
        {
            Tables = tables.AsReadOnly();
            Warnings = warnings.AsReadOnly();
            SourceFiles = sourceFiles.AsReadOnly();
            SuccessfullyReadSourcePaths = successfulPaths.AsReadOnly();
        }
    }

    /// <summary>收集多个文件或目录中的工作簿，隔离读取失败并在导出前统一检查表名冲突。</summary>
    public static class WorkbookScanner
    {
        private const string WorkbookLabel = "（工作簿）";
        private static readonly StringComparer PathComparer = Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        /// <summary>递归扫描指定目录或读取指定工作簿，返回全部工作表的校验快照，不写入源文件。</summary>
        /// <param name="projectRoot">Unity 工程根目录，必须存在 Assets 子目录。</param>
        /// <param name="sourcePaths">工程相对或绝对的文件、目录路径，可混合且允许重复。</param>
        /// <returns>包含成功表、错误条目、锁文件或链接跳过提示的完整扫描结果。</returns>
        /// <exception cref="ArgumentNullException">源路径集合为空引用。</exception>
        /// <exception cref="ArgumentException">工程根目录无效或不含 Assets 目录。</exception>
        /// <exception cref="IOException">工程根目录无法规范化。</exception>
        public static WorkbookScanResult Scan(string projectRoot, IEnumerable<string> sourcePaths)
        {
            if (sourcePaths == null) throw new ArgumentNullException(nameof(sourcePaths));
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("工程根目录不能为空。", nameof(projectRoot));
            string root = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(Path.Combine(root, "Assets")))
                throw new ArgumentException("工程根目录必须包含已存在的 Assets 目录。", nameof(projectRoot));

            var tables = new List<ScannedTable>();
            var warnings = new List<string>();
            var files = new HashSet<string>(PathComparer);
            var inputs = new HashSet<string>(PathComparer);
            var visitedDirectories = new HashSet<string>(PathComparer);
            foreach (string input in sourcePaths)
            {
                string fullPath;
                try
                {
                    if (string.IsNullOrWhiteSpace(input))
                        throw new ArgumentException("源路径不能为空，请添加 .xlsx 文件或源目录。");
                    string normalized = input.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    fullPath = Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(root, normalized));
                    if (!inputs.Add(fullPath)) continue;
                }
                catch (Exception exception)
                {
                    tables.Add(Failure(SafeDiagnosticPath(input), "源路径无效：" + exception.Message));
                    continue;
                }

                try
                {
                    // 直接输入链接内的子目录时也检查祖先，避免从目录联接间接进入其他树。
                    string linkedPath = FindReparsePoint(fullPath);
                    if (linkedPath != null)
                    {
                        warnings.Add("已跳过符号链接或目录联接：" + linkedPath);
                        continue;
                    }
                    FileAttributes attributes = File.GetAttributes(fullPath);
                    if ((attributes & FileAttributes.Directory) != 0)
                        CollectDirectory(fullPath, files, visitedDirectories, tables, warnings);
                    else
                        CollectFile(fullPath, true, files, tables, warnings);
                }
                catch (Exception exception)
                {
                    tables.Add(Failure(fullPath, "无法访问源路径：" + exception.Message));
                }
            }

            var sourceFiles = files.OrderBy(path => path, PathComparer).ThenBy(path => path, StringComparer.Ordinal).ToList();
            var successfulPaths = new List<string>();
            foreach (string file in sourceFiles)
            {
                WorkbookData workbook;
                try
                {
                    workbook = XlsxWorkbookReader.Read(file);
                    successfulPaths.Add(file);
                }
                catch (Exception exception)
                {
                    tables.Add(Failure(file, "读取工作簿失败：" + exception.Message));
                    continue;
                }

                foreach (RawSheet sheet in workbook.Sheets)
                {
                    try
                    {
                        ValidationResult validation = TableValidator.Validate(sheet);
                        tables.Add(new ScannedTable(file, sheet.Name, sheet, validation.Table, validation.Issues));
                    }
                    catch (Exception exception)
                    {
                        tables.Add(new ScannedTable(file, sheet.Name, sheet, null,
                            new[] { new ValidationIssue(file, sheet.Name, 1, 1, "校验工作表失败：" + exception.Message) }));
                    }
                }
            }

            RejectDuplicateNames(tables);
            tables = tables.OrderBy(table => table.SourcePath, PathComparer)
                .ThenBy(table => table.SourcePath, StringComparer.Ordinal).ToList();
            warnings = warnings.Distinct(StringComparer.Ordinal).OrderBy(message => message, StringComparer.Ordinal).ToList();
            return new WorkbookScanResult(tables, warnings, sourceFiles, successfulPaths);
        }

        /// <summary>使用显式栈遍历普通目录，逐项隔离访问失败，防止链接循环及重复目录扫描。</summary>
        private static void CollectDirectory(string directory, HashSet<string> files, HashSet<string> visited, List<ScannedTable> failures, List<string> warnings)
        {
            var pending = new Stack<string>();
            pending.Push(directory);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                if (!visited.Add(current)) continue;
                string[] entries;
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        warnings.Add("已跳过符号链接或目录联接：" + current);
                        continue;
                    }
                    entries = Directory.GetFileSystemEntries(current);
                    Array.Sort(entries, PathComparer);
                }
                catch (Exception exception)
                {
                    failures.Add(Failure(current, "无法扫描源目录：" + exception.Message));
                    continue;
                }

                foreach (string entry in entries)
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            warnings.Add("已跳过符号链接或目录联接：" + entry);
                        else if ((attributes & FileAttributes.Directory) != 0)
                            pending.Push(entry);
                        else
                            CollectFile(entry, false, files, failures, warnings);
                    }
                    catch (Exception exception)
                    {
                        failures.Add(Failure(entry, "无法访问扫描项：" + exception.Message));
                    }
                }
            }
        }

        /// <summary>只收集 .xlsx，显式选择非支持文件时报错，Excel 临时锁文件始终跳过。</summary>
        private static void CollectFile(string path, bool explicitlySelected, HashSet<string> files, List<ScannedTable> failures, List<string> warnings)
        {
            if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                if (explicitlySelected) failures.Add(Failure(path, "仅支持 .xlsx 文件；也可以添加包含工作簿的目录。"));
                return;
            }
            if (Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            {
                warnings.Add("已跳过 Excel 临时锁文件：" + path);
                return;
            }
            files.Add(path);
        }

        /// <summary>冲突双方都失去可导出对象，避免处理顺序决定哪一个同名表覆盖目标。</summary>
        private static void RejectDuplicateNames(List<ScannedTable> tables)
        {
            var duplicateGroups = tables.Where(table => table.Sheet != null)
                .GroupBy(table => table.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1);
            var conflicts = new Dictionary<ScannedTable, string>();
            foreach (var group in duplicateGroups)
            {
                string sources = string.Join("；", group.Select(table => table.SourcePath).Distinct(PathComparer).OrderBy(path => path, PathComparer));
                foreach (ScannedTable table in group)
                    conflicts.Add(table, "表名在扫描范围内重复（不区分大小写），所有同名表均停止导出。冲突来源：" + sources);
            }
            for (int index = 0; index < tables.Count; index++)
            {
                ScannedTable table = tables[index];
                string reason;
                if (!conflicts.TryGetValue(table, out reason)) continue;
                var issues = new List<ValidationIssue>(table.Issues)
                {
                    new ValidationIssue(table.SourcePath, table.Name, 1, 1, reason)
                };
                tables[index] = new ScannedTable(table.SourcePath, table.Name, table.Sheet, null, issues);
            }
        }

        /// <summary>检查输入路径及其祖先上的重解析标记；缺失目标仍交由调用方报告路径错误。</summary>
        private static string FindReparsePoint(string path)
        {
            string current = path;
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return current;
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                current = Path.GetDirectoryName(current);
            }
            return null;
        }

        /// <summary>生成具有统一工作簿位置的失败条目，使单个文件问题可以进入同一错误列表。</summary>
        private static ScannedTable Failure(string path, string message)
        {
            return new ScannedTable(path, WorkbookLabel, null, null,
                new[] { new ValidationIssue(path, WorkbookLabel, 1, 1, message) });
        }

        /// <summary>替换无效路径字符，避免错误展示再次触发 Path.GetFileName 异常。</summary>
        private static string SafeDiagnosticPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "（未设置源路径）";
            var invalid = new HashSet<char>(Path.GetInvalidPathChars());
            return new string(path.Select(character => invalid.Contains(character) ? '�' : character).ToArray());
        }
    }
}
