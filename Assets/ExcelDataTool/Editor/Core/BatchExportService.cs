using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExcelDataTool.Editor.Core
{
    /// <summary>保存一个工作表的导出结果或明确的失败原因。</summary>
    public sealed class TableExportOutcome
    {
        /// <summary>源工作簿完整路径，或无法读取的源路径说明。</summary>
        public string SourcePath { get; }

        /// <summary>工作表名，工作簿级失败时为“（工作簿）”。</summary>
        public string Name { get; }

        /// <summary>是否成功完成该表导出；全部内容未变化仍视作成功。</summary>
        public bool Success { get; }

        /// <summary>输入校验、命名冲突或目标写入错误，均包含可显示的位置。</summary>
        public IReadOnlyList<ValidationIssue> Issues { get; }

        /// <summary>成功时的新增、更新及跳过文件明细；失败时为 null。</summary>
        public TableExportResult ExportResult { get; }

        /// <summary>固定单表导出结果及错误集合。</summary>
        internal TableExportOutcome(string sourcePath, string name, TableExportResult result, IEnumerable<ValidationIssue> issues)
        {
            SourcePath = sourcePath;
            Name = name;
            ExportResult = result;
            Issues = new List<ValidationIssue>(issues).AsReadOnly();
            Success = result != null && Issues.Count == 0;
        }
    }

    /// <summary>汇总一次当前表或批量导出的真实结果，不把失败表计为成功。</summary>
    public sealed class BatchExportResult
    {
        /// <summary>本次请求范围内每张表的成功或失败结果。</summary>
        public IReadOnlyList<TableExportOutcome> Tables { get; }

        /// <summary>扫描跳过、过时生成物和可恢复写入提醒。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>成功处理的表数，包括全部输出内容未变化的表。</summary>
        public int SuccessCount { get; }

        /// <summary>校验失败、解析失败、未找到选择或写入失败的条目数。</summary>
        public int FailureCount { get; }

        /// <summary>实际新增或改写的 JSON、行类型及表类型文件总数，不计归属记录。</summary>
        public int WrittenCount { get; }

        /// <summary>因内容完全一致而跳过写入的生成文件总数，不计归属记录。</summary>
        public int SkippedCount { get; }

        /// <summary>根据已完成的逐表结果计算统计，确保界面与实际写入行为一致。</summary>
        internal BatchExportResult(List<TableExportOutcome> tables, List<string> warnings)
        {
            Tables = tables.AsReadOnly();
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList().AsReadOnly();
            SuccessCount = tables.Count(table => table.Success);
            FailureCount = tables.Count - SuccessCount;
            WrittenCount = tables.Where(table => table.Success).Sum(table => table.ExportResult.WrittenCount);
            SkippedCount = tables.Where(table => table.Success).Sum(table => table.ExportResult.SkippedCount);
        }
    }

    /// <summary>执行已经完成全局冲突检查的扫描快照，逐表隔离失败并汇总文件写入结果。</summary>
    public static class BatchExportService
    {
        /// <summary>导出扫描中的全部表或一个指定表；单表选择仍遵守完整扫描中的冲突结果。</summary>
        /// <param name="scan">导出前重新扫描得到的完整结果。</param>
        /// <param name="projectRoot">包含 Assets 目录的 Unity 工程根目录。</param>
        /// <param name="jsonDirectory">工程 Assets 内的 JSON 输出目录。</param>
        /// <param name="codeDirectory">工程 Assets 内的运行时 C# 输出目录。</param>
        /// <param name="generatedNamespace">生成的 C# 命名空间，由导出器统一校验。</param>
        /// <param name="selectedSourcePath">只导出当前表时的源路径；与表名同时省略则批量导出。</param>
        /// <param name="selectedSheetName">只导出当前表时的实际工作表名。</param>
        /// <returns>逐表成功、失败、改写与未变化文件的完整摘要；无效表不会调用写入器。</returns>
        /// <exception cref="ArgumentNullException">扫描结果为空引用。</exception>
        public static BatchExportResult Export(
            WorkbookScanResult scan,
            string projectRoot,
            string jsonDirectory,
            string codeDirectory,
            string generatedNamespace,
            string selectedSourcePath = null,
            string selectedSheetName = null)
        {
            if (scan == null) throw new ArgumentNullException(nameof(scan));
            var outcomes = new List<TableExportOutcome>();
            var warnings = new List<string>(scan.Warnings);
            IEnumerable<ScannedTable> requested = scan.Tables;
            if (selectedSourcePath != null || selectedSheetName != null)
            {
                string diagnosticSource = "（所选工作簿）";
                try
                {
                    if (string.IsNullOrWhiteSpace(selectedSourcePath) || string.IsNullOrWhiteSpace(selectedSheetName))
                        throw new ArgumentException("导出当前表需要同时指定源工作簿和工作表。");
                    string source = Path.GetFullPath(Path.IsPathRooted(selectedSourcePath)
                        ? selectedSourcePath : Path.Combine(projectRoot, selectedSourcePath));
                    diagnosticSource = source;
                    StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    requested = scan.Tables.Where(table => string.Equals(table.SourcePath, source, comparison) &&
                        string.Equals(table.Name, selectedSheetName, StringComparison.Ordinal)).ToArray();
                    if (!requested.Any())
                        throw new ArgumentException("重新扫描后未找到所选工作表，请检查文件是否移动或表名是否修改。");
                }
                catch (Exception exception)
                {
                    outcomes.Add(Failure(diagnosticSource, selectedSheetName ?? "（工作簿）", exception.Message));
                    requested = Enumerable.Empty<ScannedTable>();
                }
            }

            foreach (ScannedTable table in requested)
            {
                if (!table.IsValid)
                {
                    outcomes.Add(new TableExportOutcome(table.SourcePath, table.Name, null, table.Issues));
                    continue;
                }
                try
                {
                    TableExportResult result = TableExporter.ExportDetailed(table.Table, projectRoot, jsonDirectory, codeDirectory, generatedNamespace);
                    outcomes.Add(new TableExportOutcome(table.SourcePath, table.Name, result, Array.Empty<ValidationIssue>()));
                    warnings.AddRange(result.Warnings);
                }
                catch (Exception exception)
                {
                    // 写入器负责单表回滚；这里只记录失败，让其余互不冲突的有效表继续导出。
                    outcomes.Add(Failure(table.SourcePath, table.Name, "导出失败：" + exception.Message));
                }
            }

            if (outcomes.Count == 0)
                outcomes.Add(Failure("（未选择工作簿）", "（工作簿）", "没有可导出的工作表，请添加包含有效数据表的 .xlsx 文件或目录。"));
            try
            {
                warnings.AddRange(TableExporter.FindObsoleteOutputs(projectRoot,
                    scan.Tables.Where(table => table.Sheet != null).Select(table => table.Sheet), scan.SuccessfullyReadSourcePaths));
            }
            catch (Exception exception)
            {
                warnings.Add("无法完成过时生成物检查，已有文件均保留：" + exception.Message);
            }
            return new BatchExportResult(outcomes, warnings);
        }

        /// <summary>将文件写入或选择错误统一包装为 A1 位置，便于窗口集中显示。</summary>
        private static TableExportOutcome Failure(string sourcePath, string name, string message)
        {
            return new TableExportOutcome(sourcePath, name, null,
                new[] { new ValidationIssue(sourcePath, name, 1, 1, message) });
        }
    }
}
