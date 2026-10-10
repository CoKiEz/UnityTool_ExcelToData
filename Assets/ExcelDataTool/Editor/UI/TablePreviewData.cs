using System;
using System.Collections.Generic;
using System.Linq;
using ExcelDataTool.Editor.Core;

namespace ExcelDataTool.Editor.UI
{
    /// <summary>限定预览规模并保留真实 Excel 坐标的只读视图，允许预览尚未通过校验的原始数据。</summary>
    public sealed class TablePreviewData
    {
        private readonly RawSheet sheet;

        /// <summary>预览包含的实际 Excel 行号：三行表头及最多指定数量的非空数据行。</summary>
        public IReadOnlyList<int> RowNumbers { get; }
        /// <summary>源表包含内容的最大列号。</summary>
        public int ColumnCount { get; }
        /// <summary>当前预览实际显示的列数。</summary>
        public int VisibleColumnCount { get; }
        /// <summary>源表全部非空数据行数。</summary>
        public int TotalDataRowCount { get; }

        /// <summary>构造受限视图，避免根据稀疏坐标创建大量空白行。</summary>
        private TablePreviewData(RawSheet sheet, int maximumDataRows, int maximumColumns)
        {
            this.sheet = sheet;
            ColumnCount = sheet.Rows.Values.SelectMany(row => row).Where(cell => HasContent(cell.Value))
                .Select(cell => cell.Key).DefaultIfEmpty(0).Max();
            VisibleColumnCount = Math.Min(ColumnCount, maximumColumns);
            var dataRows = sheet.Rows.Where(row => row.Key >= 4 && row.Value.Values.Any(HasContent))
                .Select(row => row.Key).ToList();
            TotalDataRowCount = dataRows.Count;
            RowNumbers = new[] { 1, 2, 3 }.Concat(dataRows.Take(maximumDataRows)).ToList().AsReadOnly();
        }

        /// <summary>创建纯数据预览模型，不读取磁盘、不写设置且不生成 UI 元素。</summary>
        /// <param name="sheet">需要预览的原始工作表。</param>
        /// <param name="maximumDataRows">最多显示的数据行数量，表头另计三行。</param>
        /// <param name="maximumColumns">最多显示的列数。</param>
        /// <returns>保留字段名、类型、说明及真实行号的受限模型。</returns>
        /// <exception cref="ArgumentNullException">工作表为空。</exception>
        /// <exception cref="ArgumentOutOfRangeException">预览上限不是正数。</exception>
        public static TablePreviewData Create(RawSheet sheet, int maximumDataRows = 100, int maximumColumns = 30)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (maximumDataRows < 1) throw new ArgumentOutOfRangeException(nameof(maximumDataRows));
            if (maximumColumns < 1) throw new ArgumentOutOfRangeException(nameof(maximumColumns));
            return new TablePreviewData(sheet, maximumDataRows, maximumColumns);
        }

        /// <summary>判断错误坐标是否位于当前可见预览范围。</summary>
        /// <param name="row">从一开始的 Excel 行号。</param>
        /// <param name="column">从一开始的 Excel 列号。</param>
        /// <returns>坐标同时位于已保留行和可见列中时为 true。</returns>
        public bool ContainsCell(int row, int column)
        {
            return column >= 1 && column <= VisibleColumnCount && RowNumbers.Contains(row);
        }

        /// <summary>读取原始单元格文本，允许错误位置位于受限预览之外。</summary>
        /// <param name="row">从一开始的 Excel 行号。</param>
        /// <param name="column">从一开始的 Excel 列号。</param>
        /// <returns>原始文本；未存储的位置返回空字符串。</returns>
        /// <exception cref="ArgumentOutOfRangeException">行号或列号小于一。</exception>
        public string GetCellText(int row, int column)
        {
            if (row < 1) throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 1) throw new ArgumentOutOfRangeException(nameof(column));
            return sheet.GetCell(row, column).Text;
        }

        /// <summary>把一开始的行列号转换为 A1 形式，支持 AA 等多字母列。</summary>
        /// <param name="row">Excel 行号。</param>
        /// <param name="column">Excel 列号。</param>
        /// <returns>标准 Excel 单元格地址。</returns>
        /// <exception cref="ArgumentOutOfRangeException">行号或列号小于一。</exception>
        public static string GetCellAddress(int row, int column)
        {
            if (row < 1) throw new ArgumentOutOfRangeException(nameof(row));
            if (column < 1) throw new ArgumentOutOfRangeException(nameof(column));
            string letters = string.Empty;
            while (column > 0)
            {
                column--;
                letters = (char)('A' + column % 26) + letters;
                column /= 26;
            }
            return letters + row;
        }

        /// <summary>判断单元格是否实际含内容，缓存缺失等读取错误同样计入预览。</summary>
        private static bool HasContent(RawCell cell)
        {
            return cell != null && (!string.IsNullOrEmpty(cell.Text) || !string.IsNullOrEmpty(cell.Error));
        }
    }
}
