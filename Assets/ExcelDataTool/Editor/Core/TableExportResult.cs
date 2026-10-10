using System.Collections.Generic;
using System.Linq;

namespace ExcelDataTool.Editor.Core
{
    /// <summary>保存单表导出的文件、实际写入与增量跳过结果；所有列表均为不可修改的快照。</summary>
    public sealed class TableExportResult
    {
        /// <summary>获取 JSON、行类型和表类型的三个工程相对路径，不包含内部所有权记录。</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>获取本次因内容变化或目标缺失而实际写入的生成文件路径。</summary>
        public IReadOnlyList<string> WrittenFiles { get; }

        /// <summary>获取本次内容相同、保留原有字节及时间戳的生成文件路径。</summary>
        public IReadOnlyList<string> SkippedFiles { get; }

        /// <summary>获取导出完成后仍需要用户留意的提示。</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>获取实际写入的业务文件数，不包含内部所有权记录。</summary>
        public int WrittenCount => WrittenFiles.Count;

        /// <summary>获取因内容相同而跳过的业务文件数。</summary>
        public int SkippedCount => SkippedFiles.Count;

        /// <summary>复制导出结果，避免调用方或内部可变列表影响已返回的摘要。</summary>
        /// <param name="files">全部三个生成文件的路径。</param>
        /// <param name="writtenFiles">实际写入的生成文件路径。</param>
        /// <param name="skippedFiles">因内容相同而跳过的生成文件路径。</param>
        /// <param name="warnings">可供窗口显示的提示。</param>
        internal TableExportResult(IEnumerable<string> files, IEnumerable<string> writtenFiles,
            IEnumerable<string> skippedFiles, IEnumerable<string> warnings)
        {
            Files = files.ToList().AsReadOnly();
            WrittenFiles = writtenFiles.ToList().AsReadOnly();
            SkippedFiles = skippedFiles.ToList().AsReadOnly();
            Warnings = warnings.ToList().AsReadOnly();
        }
    }
}
