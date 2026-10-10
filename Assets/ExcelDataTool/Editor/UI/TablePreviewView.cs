using System;
using System.Collections.Generic;
using System.Linq;
using ExcelDataTool.Editor.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelDataTool.Editor.UI
{
    /// <summary>绘制有明确行列上限的原始表格，并高亮用户选中的错误单元格。</summary>
    internal sealed class TablePreviewView : VisualElement
    {
        private readonly Label caption;
        private readonly HelpBox location;
        private readonly ScrollView grid;

        /// <summary>创建可横向及纵向滚动的预览区域，不访问源文件。</summary>
        internal TablePreviewView()
        {
            caption = new Label("选择工作表后显示原始数据，包括未通过校验的值。");
            caption.style.whiteSpace = WhiteSpace.Normal;
            Add(caption);
            location = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            location.style.display = DisplayStyle.None;
            Add(location);
            grid = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            grid.style.height = 265;
            grid.style.marginTop = 5;
            grid.style.borderTopWidth = 1;
            grid.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            Add(grid);
        }

        /// <summary>显示字段名、类型、说明和受限数据行，并定位可见错误。</summary>
        /// <param name="sheet">原始表，读取失败时可以为空。</param>
        /// <param name="issues">该表的错误，用于给单元格着色。</param>
        /// <param name="focusedIssue">用户点击的错误，允许位于预览范围外。</param>
        internal void Display(RawSheet sheet, IReadOnlyList<ValidationIssue> issues, ValidationIssue focusedIssue)
        {
            grid.Clear();
            location.style.display = DisplayStyle.None;
            if (sheet == null)
            {
                caption.text = "选择已读取的工作表后显示预览；读取失败的原因保留在错误列表。";
                return;
            }
            TablePreviewData data = TablePreviewData.Create(sheet);
            caption.text = sheet.Name + " · " + data.ColumnCount + " 列 · " + data.TotalDataRowCount
                + " 行数据；显示前 " + Math.Min(100, data.TotalDataRowCount) + " 行、前 " + data.VisibleColumnCount
                + " 列。前三行分别为字段名、类型、说明。";
            var errors = new HashSet<string>((issues ?? Array.Empty<ValidationIssue>())
                .Where(issue => issue.Row > 0 && issue.Column > 0)
                .Select(issue => TablePreviewData.GetCellAddress(issue.Row, issue.Column)), StringComparer.Ordinal);
            string focusedAddress = focusedIssue == null ? null : TablePreviewData.GetCellAddress(Math.Max(1, focusedIssue.Row), Math.Max(1, focusedIssue.Column));
            Label selectedCell = null;
            foreach (int rowNumber in data.RowNumbers)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexShrink = 0;
                row.style.height = 27;
                row.style.width = 92 + data.VisibleColumnCount * 144;
                string rowLabel = rowNumber == 1 ? "1  字段名" : rowNumber == 2 ? "2  类型" : rowNumber == 3 ? "3  说明" : rowNumber.ToString();
                Label number = CreateCell(rowLabel, 92);
                number.style.unityFontStyleAndWeight = FontStyle.Bold;
                number.style.backgroundColor = new Color(0.4f, 0.4f, 0.4f, 0.18f);
                row.Add(number);
                for (int column = 1; column <= data.VisibleColumnCount; column++)
                {
                    string address = TablePreviewData.GetCellAddress(rowNumber, column);
                    string raw = data.GetCellText(rowNumber, column);
                    Label cell = CreateCell(raw.Length <= 160 ? raw : raw.Substring(0, 160) + "…", 144);
                    cell.name = "preview-cell-" + address;
                    cell.tooltip = address + "\n" + (raw.Length <= 4096 ? raw : raw.Substring(0, 4096) + "…");
                    if (rowNumber <= 3) cell.style.unityFontStyleAndWeight = FontStyle.Bold;
                    if (errors.Contains(address)) cell.style.backgroundColor = new Color(0.7f, 0.16f, 0.12f, 0.22f);
                    if (address == focusedAddress)
                    {
                        cell.style.backgroundColor = new Color(0.9f, 0.58f, 0.08f, 0.55f);
                        selectedCell = cell;
                    }
                    row.Add(cell);
                }
                grid.Add(row);
            }
            if (focusedIssue != null)
            {
                location.style.display = DisplayStyle.Flex;
                location.messageType = HelpBoxMessageType.Warning;
                string rawValue = data.GetCellText(Math.Max(1, focusedIssue.Row), Math.Max(1, focusedIssue.Column));
                string visibleValue = rawValue.Length <= 512 ? rawValue : rawValue.Substring(0, 512) + "…（原值已截断）";
                location.text = "定位 " + focusedAddress + "：" + focusedIssue.Message
                    + (data.ContainsCell(focusedIssue.Row, focusedIssue.Column) ? "（已高亮）" : "（超出当前预览范围，请在 Excel 中打开此位置）")
                    + "\n原始值：" + (rawValue.Length == 0 ? "（空）" : visibleValue);
                if (selectedCell != null)
                {
                    // 布局完成后再滚动，避免首次创建时单元格尚无几何位置。
                    Label destination = selectedCell;
                    grid.schedule.Execute(() =>
                    {
                        // 快速切换表时旧单元格已移除，不能继续使用它计算滚动位置。
                        if (grid.Contains(destination)) grid.ScrollTo(destination);
                    });
                }
            }
        }

        /// <summary>创建固定宽度的单元格，长文本通过省略号和悬浮提示呈现。</summary>
        private static Label CreateCell(string text, float width)
        {
            var cell = new Label(text);
            cell.style.width = width;
            cell.style.flexShrink = 0;
            cell.style.paddingLeft = 7;
            cell.style.paddingRight = 4;
            cell.style.unityTextAlign = TextAnchor.MiddleLeft;
            cell.style.overflow = Overflow.Hidden;
            cell.style.textOverflow = TextOverflow.Ellipsis;
            cell.style.whiteSpace = WhiteSpace.NoWrap;
            cell.style.borderRightWidth = 1;
            cell.style.borderBottomWidth = 1;
            cell.style.borderRightColor = new Color(0.4f, 0.4f, 0.4f, 0.2f);
            cell.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f, 0.2f);
            return cell;
        }
    }
}
