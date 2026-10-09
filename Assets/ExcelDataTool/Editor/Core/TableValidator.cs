using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExcelDataTool.Editor.Core
{
    /// <summary>描述一项具有源文件、工作表和单元格坐标的校验错误。</summary>
    public sealed class ValidationIssue
    {
        /// <summary>源工作簿路径。</summary>
        public string SourcePath { get; }
        /// <summary>工作表名称。</summary>
        public string SheetName { get; }
        /// <summary>从一开始计数的 Excel 行号。</summary>
        public int Row { get; }
        /// <summary>从一开始计数的 Excel 列号。</summary>
        public int Column { get; }
        /// <summary>可供用户修复数据的错误说明。</summary>
        public string Message { get; }

        /// <summary>创建带有准确源位置的错误。</summary>
        /// <param name="sourcePath">源文件路径。</param>
        /// <param name="sheetName">工作表名称。</param>
        /// <param name="row">Excel 行号。</param>
        /// <param name="column">Excel 列号。</param>
        /// <param name="message">错误原因。</param>
        public ValidationIssue(string sourcePath, string sheetName, int row, int column, string message)
        {
            SourcePath = sourcePath;
            SheetName = sheetName;
            Row = row;
            Column = column;
            Message = message;
        }

        /// <summary>格式化文件、工作表、A1 坐标及错误原因。</summary>
        /// <returns>适合窗口和控制台显示的错误文本。</returns>
        public override string ToString()
        {
            int index = Math.Max(1, Column);
            string letters = string.Empty;
            while (index > 0)
            {
                index--;
                letters = (char)('A' + index % 26) + letters;
                index /= 26;
            }
            return $"{Path.GetFileName(SourcePath)} / {SheetName} / {letters}{Row}: {Message}";
        }
    }

    /// <summary>保存已通过名称与类型校验的字段定义。</summary>
    public sealed class TableColumn
    {
        /// <summary>与 JSON 键一致的 C# 字段名。</summary>
        public string Name { get; }
        /// <summary>协议规定的 C# 基础类型名。</summary>
        public string TypeName { get; }
        /// <summary>第三行的字段说明，允许为空。</summary>
        public string Description { get; }
        /// <summary>源表中的列号，从一开始。</summary>
        public int ExcelColumn { get; }

        /// <summary>记录已经校验过的列信息。</summary>
        internal TableColumn(string name, string typeName, string description, int excelColumn)
        {
            Name = name;
            TypeName = typeName;
            Description = description;
            ExcelColumn = excelColumn;
        }
    }

    /// <summary>保存可直接生成 JSON 的强类型中间数据，不依赖任何生成程序集。</summary>
    public sealed class ValidatedTable
    {
        /// <summary>用于生成文件名与类型名的表名。</summary>
        public string Name { get; }
        /// <summary>用于识别输出归属的源文件路径。</summary>
        public string SourcePath { get; }
        /// <summary>按照 Excel 列顺序排列的字段。</summary>
        public IReadOnlyList<TableColumn> Columns { get; }
        /// <summary>按照 Excel 行顺序排列的基础类型数据。</summary>
        public IReadOnlyList<object[]> Rows { get; }

        /// <summary>仅由校验器在所有检查通过后创建可导出对象。</summary>
        internal ValidatedTable(RawSheet sheet, List<TableColumn> columns, List<object[]> rows)
        {
            Name = sheet.Name;
            SourcePath = sheet.SourcePath;
            Columns = columns.AsReadOnly();
            Rows = rows.AsReadOnly();
        }
    }

    /// <summary>包含校验结果；存在任意错误时不提供可导出数据。</summary>
    public sealed class ValidationResult
    {
        /// <summary>全部有效时为可导出表，否则为 null。</summary>
        public ValidatedTable Table { get; }
        /// <summary>本次发现的所有输入错误。</summary>
        public IReadOnlyList<ValidationIssue> Issues { get; }
        /// <summary>是否通过全部校验。</summary>
        public bool IsValid => Table != null && Issues.Count == 0;

        /// <summary>把错误列表固定为只读视图。</summary>
        internal ValidationResult(ValidatedTable table, List<ValidationIssue> issues)
        {
            Table = table;
            Issues = issues.AsReadOnly();
        }
    }

    /// <summary>执行三行表头、字段类型、必填值和唯一 ID 的纯数据校验。</summary>
    public static class TableValidator
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(
            ("abstract as base bool break byte case catch char checked class const continue decimal default " +
             "delegate do double else enum event explicit extern false finally fixed float for foreach goto " +
             "if implicit in int interface internal is lock long namespace new null object operator out override " +
             "params private protected public readonly ref return sbyte sealed short sizeof stackalloc static " +
             "string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual " +
             "void volatile while __arglist __makeref __reftype __refvalue").Split(' '), StringComparer.Ordinal);
        private static readonly HashSet<string> Types = new HashSet<string>(
            new[] { "int", "float", "double", "bool", "string" }, StringComparer.Ordinal);

        /// <summary>校验一个工作表并将合法单元格转换成对应的 .NET 基础类型。</summary>
        /// <param name="sheet">只读解析后的源表。</param>
        /// <returns>包含全部错误或可导出表的结果；此方法不写入任何文件。</returns>
        /// <exception cref="ArgumentNullException">传入的工作表为空。</exception>
        public static ValidationResult Validate(RawSheet sheet)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            var issues = new List<ValidationIssue>();
            Action<int, int, string> error = (row, column, reason) =>
                issues.Add(new ValidationIssue(sheet.SourcePath, sheet.Name, row, column, reason));
            if (string.IsNullOrEmpty(sheet.Name) || !Regex.IsMatch(sheet.Name, @"\A[A-Z][A-Za-z0-9_]*\z") ||
                Regex.IsMatch(sheet.Name, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase))
                error(1, 1, "表名必须是 PascalCase 标识符，且不能是系统保留文件名。");

            // 不使用工作表 dimension 推断范围，避免 Excel 残留格式造成大量空列。
            int width = sheet.Rows.Values.SelectMany(row => row)
                .Where(cell => HasContent(cell.Value)).Select(cell => cell.Key).DefaultIfEmpty(0).Max();
            if (width == 0) error(1, 1, "工作表为空，缺少三行表头和 id 字段。");
            var columns = new List<TableColumn>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            int idColumn = -1;
            for (int column = 1; column <= width; column++)
            {
                RawCell nameCell = sheet.GetCell(1, column);
                RawCell typeCell = sheet.GetCell(2, column);
                RawCell descriptionCell = sheet.GetCell(3, column);
                string name = nameCell.Text ?? string.Empty;
                string type = typeCell.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(nameCell.Error)) error(1, column, nameCell.Error);
                if (!string.IsNullOrEmpty(typeCell.Error)) error(2, column, typeCell.Error);
                if (!string.IsNullOrEmpty(descriptionCell.Error)) error(3, column, descriptionCell.Error);
                if (!Regex.IsMatch(name, @"\A[a-z][A-Za-z0-9_]*\z") || Keywords.Contains(name))
                    error(1, column, "字段名必须是合法且非 C# 保留字的 lowerCamelCase 标识符，不能为空。");
                if (!names.Add(name)) error(1, column, $"字段名重复：{name}。");
                if (!Types.Contains(type)) error(2, column, $"不支持类型“{type}”，仅接受 int / float / double / bool / string 的精确拼写。");
                if (name == "id")
                {
                    idColumn = column - 1;
                    if (type != "int") error(2, column, "id 字段的类型必须为 int。");
                }
                columns.Add(new TableColumn(name, type, descriptionCell.Text ?? string.Empty, column));
            }
            if (idColumn < 0) error(1, 1, "缺少必需的 id 字段（类型必须是 int）。");

            var records = new List<object[]>();
            var ids = new Dictionary<int, int>();
            foreach (var row in sheet.Rows)
            {
                if (row.Key < 4 || !row.Value.Values.Any(HasContent)) continue;
                var values = new object[columns.Count];
                for (int index = 0; index < columns.Count; index++)
                {
                    RawCell cell = sheet.GetCell(row.Key, index + 1);
                    if (!string.IsNullOrEmpty(cell.Error))
                    {
                        error(row.Key, index + 1, cell.Error);
                        continue;
                    }
                    if (!Types.Contains(columns[index].TypeName)) continue;
                    if (!TryConvert(cell, columns[index].TypeName, out object value))
                        error(row.Key, index + 1, $"值“{cell.Text}”不能转换为 {columns[index].TypeName}；非字符串必填，数值必须有限且在类型范围内。");
                    else
                        values[index] = value;
                }
                if (idColumn >= 0 && values[idColumn] is int id)
                {
                    if (ids.TryGetValue(id, out int firstRow))
                        error(row.Key, idColumn + 1, $"重复 ID {id}，首次出现在第 {firstRow} 行。");
                    else
                        ids.Add(id, row.Key);
                }
                records.Add(values);
            }
            return new ValidationResult(issues.Count == 0 ? new ValidatedTable(sheet, columns, records) : null, issues);
        }

        /// <summary>判断单元格是否需要参与表结构或数据行校验。</summary>
        private static bool HasContent(RawCell cell)
        {
            return cell != null && (!string.IsNullOrEmpty(cell.Text) || !string.IsNullOrEmpty(cell.Error));
        }

        /// <summary>使用固定区域规则转换数值，并拒绝布尔单元格被误当成数字。</summary>
        private static bool TryConvert(RawCell cell, string type, out object value)
        {
            value = null;
            string text = cell.Text ?? string.Empty;
            if (type == "string") { value = text; return true; }
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (type == "bool")
            {
                if (!bool.TryParse(text, out bool boolean)) return false;
                value = boolean;
                return true;
            }
            if (cell.IsBoolean) return false;
            if (type == "int")
            {
                if (!TryExactInteger(text, out int integer)) return false;
                value = integer;
                return true;
            }
            if (type == "float")
            {
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                    float.IsNaN(number) || float.IsInfinity(number)) return false;
                value = number;
                return true;
            }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double precise) ||
                double.IsNaN(precise) || double.IsInfinity(precise)) return false;
            value = precise;
            return true;
        }

        /// <summary>按十进制文本检查整数，避免浮点或 decimal 舍入把微小小数误判为整数。</summary>
        private static bool TryExactInteger(string text, out int value)
        {
            value = 0;
            Match match = Regex.Match(text.Trim(), @"\A([+-]?)([0-9]+)(?:\.([0-9]+))?(?:[eE]([+-]?[0-9]+))?\z");
            if (!match.Success) return false;
            int exponent = 0;
            if (match.Groups[4].Success && !int.TryParse(match.Groups[4].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out exponent)) return false;
            string digits = (match.Groups[2].Value + match.Groups[3].Value).TrimStart('0');
            if (digits.Length == 0) return true;
            long scale = (long)exponent - match.Groups[3].Value.Length;
            while (scale < 0 && digits.EndsWith("0", StringComparison.Ordinal))
            {
                digits = digits.Substring(0, digits.Length - 1);
                scale++;
            }
            if (scale < 0 || scale > 10 || digits.Length + scale > 10) return false;
            return int.TryParse(match.Groups[1].Value + digits + new string('0', (int)scale),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
