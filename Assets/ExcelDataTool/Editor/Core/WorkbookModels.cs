using System.Collections.Generic;

namespace ExcelDataTool.Editor.Core
{
    /// <summary>
    /// 保存单元格的原始文本、读取错误及 Excel 布尔类型信息，供独立校验层转换。
    /// </summary>
    public sealed class RawCell
    {
        /// <summary>获取未按目标字段类型转换的原始文本。</summary>
        public string Text { get; }

        /// <summary>获取读取错误；没有错误时为 null。</summary>
        public string Error { get; }

        /// <summary>获取该值是否来自 Excel 原生布尔单元格。</summary>
        public bool IsBoolean { get; }

        /// <summary>创建一个原始单元格；空文本统一保存为空字符串。</summary>
        /// <param name="text">单元格原始文本。</param>
        /// <param name="error">读取错误说明，没有错误时传入 null。</param>
        /// <param name="isBoolean">是否为 Excel 原生布尔单元格。</param>
        public RawCell(string text, string error = null, bool isBoolean = false)
        {
            Text = text ?? string.Empty;
            Error = error;
            IsBoolean = isBoolean;
        }
    }

    /// <summary>
    /// 保存单张工作表的稀疏单元格；行列号均从 1 开始，与 Excel 地址一致。
    /// </summary>
    public sealed class RawSheet
    {
        private static readonly RawCell EmptyCell = new RawCell(string.Empty);

        /// <summary>获取原始工作表名。</summary>
        public string Name { get; }

        /// <summary>获取源工作簿的完整文件路径。</summary>
        public string SourcePath { get; }

        /// <summary>获取按实际行号、列号排序的稀疏单元格集合。</summary>
        public SortedDictionary<int, SortedDictionary<int, RawCell>> Rows { get; }

        /// <summary>创建尚未填充数据的原始工作表。</summary>
        /// <param name="sourcePath">源工作簿的文件路径。</param>
        /// <param name="name">原始工作表名。</param>
        public RawSheet(string sourcePath, string name)
        {
            SourcePath = sourcePath;
            Name = name;
            Rows = new SortedDictionary<int, SortedDictionary<int, RawCell>>();
        }

        /// <summary>按 Excel 行列号获取单元格，未存储的位置返回空单元格。</summary>
        /// <param name="row">从 1 开始的 Excel 行号。</param>
        /// <param name="column">从 1 开始的 Excel 列号。</param>
        /// <returns>指定位置的原始单元格，或共享的只读空单元格。</returns>
        public RawCell GetCell(int row, int column)
        {
            SortedDictionary<int, RawCell> cells;
            RawCell cell;
            return Rows.TryGetValue(row, out cells) && cells.TryGetValue(column, out cell)
                ? cell
                : EmptyCell;
        }
    }

    /// <summary>保存工作簿中可用于配置导出的工作表，不包含以 # 开头的说明表。</summary>
    public sealed class WorkbookData
    {
        /// <summary>获取源工作簿的完整文件路径。</summary>
        public string SourcePath { get; }

        /// <summary>获取按源文件顺序排列的工作表。</summary>
        public List<RawSheet> Sheets { get; }

        /// <summary>创建空的工作簿中间模型。</summary>
        /// <param name="sourcePath">源工作簿的完整文件路径。</param>
        public WorkbookData(string sourcePath)
        {
            SourcePath = sourcePath;
            Sheets = new List<RawSheet>();
        }
    }
}
