using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ExcelDataTool.Editor.Core
{
    /// <summary>
    /// 使用 .NET 的 ZIP/XML API 只读解析标准 .xlsx 配置表，不启动 Excel、不计算公式。
    /// </summary>
    public static class XlsxWorkbookReader
    {
        private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string StrictSpreadsheetNamespace = "http://purl.oclc.org/ooxml/spreadsheetml/main";
        private const long MaximumPartBytes = 32L * 1024 * 1024;
        private const long MaximumArchiveBytes = 128L * 1024 * 1024;
        private const int MaximumCells = 500000;
        private static readonly Regex EscapedCharacter = new Regex("_x([0-9A-Fa-f]{4})_", RegexOptions.CultureInvariant);

        /// <summary>
        /// 读取工作簿并保留原始文本和单元格错误；字段语义由校验层处理。
        /// </summary>
        /// <param name="path">.xlsx 文件的绝对路径，或相对当前工作目录的路径。</param>
        /// <returns>不含 # 说明表的工作簿中间模型。</returns>
        /// <exception cref="IOException">路径无效、文件无法读取或被独占锁定。</exception>
        /// <exception cref="InvalidDataException">文件不是支持的 .xlsx，结构损坏或超过读取上限。</exception>
        public static WorkbookData Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new IOException("请选择一个 .xlsx 工作簿。");

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                throw new IOException("工作簿路径无效：" + path, exception);
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("仅支持 .xlsx 工作簿：" + Path.GetFileName(fullPath));
            if (Path.GetFileName(fullPath).StartsWith("~$", StringComparison.Ordinal))
                throw new InvalidDataException("不能读取 Excel 临时锁文件，请选择原始工作簿：" + Path.GetFileName(fullPath));

            try
            {
                // 允许 Excel 同时打开文件；读取期间绝不写回源表，也不尝试修复 ZIP 内容。
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    ValidateArchive(archive);
                    return ReadWorkbook(archive, fullPath);
                }
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException(Path.GetFileName(fullPath) + "：工作簿无效。" + exception.Message, exception);
            }
            catch (XmlException exception)
            {
                throw new InvalidDataException(Path.GetFileName(fullPath) + "：工作簿 XML 已损坏或包含不支持的 DTD。" + exception.Message, exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new IOException(Path.GetFileName(fullPath) + "：没有读取权限。", exception);
            }
            catch (IOException exception)
            {
                throw new IOException(Path.GetFileName(fullPath) + "：无法读取文件，请检查文件是否存在、正在保存或被独占锁定。" + exception.Message, exception);
            }
        }

        /// <summary>限制压缩包规模并拒绝重复部件，避免模糊路径及异常内存占用。</summary>
        private static void ValidateArchive(ZipArchive archive)
        {
            long totalBytes = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (archive.Entries.Count > 10000)
                throw new InvalidDataException("压缩包部件过多，最多支持 10000 个部件。");
            foreach (var entry in archive.Entries)
            {
                if (!names.Add(entry.FullName))
                    throw new InvalidDataException("压缩包含重复部件：" + entry.FullName);
                totalBytes += entry.Length;
                if (entry.Length > MaximumPartBytes || totalBytes > MaximumArchiveBytes)
                    throw new InvalidDataException("工作簿过大：单部件最多 32 MiB，解压总量最多 128 MiB。");
            }
        }

        /// <summary>通过包关系找到工作簿及其工作表，不假定 worksheet 文件名或顺序。</summary>
        private static WorkbookData ReadWorkbook(ZipArchive archive, string path)
        {
            var rootRelations = ReadRelationships(archive, string.Empty);
            Relationship workbookRelation = FindRelationship(rootRelations, "/officeDocument", true);
            string workbookPart = ResolvePart(string.Empty, workbookRelation);
            XElement workbook = ReadXml(archive, workbookPart).Root;
            XNamespace ns = RequireSpreadsheetRoot(workbook, "workbook");
            var relations = ReadRelationships(archive, workbookPart);
            List<string> strings = ReadSharedStrings(archive, workbookPart, relations);
            List<bool> dateStyles = ReadDateStyles(archive, workbookPart, relations);
            var result = new WorkbookData(path);
            var sheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            XElement sheets = workbook.Element(ns + "sheets");
            if (sheets == null)
                throw new InvalidDataException("缺少工作表列表。");

            int cellCount = 0;
            foreach (XElement sheet in sheets.Elements(ns + "sheet"))
            {
                string name = (string)sheet.Attribute("name") ?? string.Empty;
                if (name.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string relationshipId = sheet.Attributes()
                    .Where(attribute => attribute.Name.LocalName == "id" && attribute.Name.NamespaceName.EndsWith("/relationships", StringComparison.Ordinal))
                    .Select(attribute => attribute.Value).FirstOrDefault();
                Relationship relation;
                if (relationshipId == null || !relations.TryGetValue(relationshipId, out relation))
                    throw new InvalidDataException("工作表 " + name + " [A1]：找不到对应的工作表部件。");
                // 图表页不含配置单元格；保留普通 worksheet，其余工作表种类不作为数据表。
                if (!relation.Type.EndsWith("/worksheet", StringComparison.Ordinal))
                    continue;
                // Windows 输出目录通常不区分大小写，源工作簿中的同名页必须提前拦截。
                if (!sheetNames.Add(name))
                    throw new InvalidDataException("工作表 " + name + " [A1]：表名重复（不区分大小写），不能生成同名输出文件。");
                result.Sheets.Add(ReadSheet(archive, ResolvePart(workbookPart, relation), path, name, strings, dateStyles, ref cellCount));
            }

            if (result.Sheets.Count == 0)
                throw new InvalidDataException("没有有效数据工作表，# 开头的工作表和图表页不会参与导出。");
            return result;
        }

        /// <summary>读取实际存在的行和单元格，保留稀疏地址以正确定位空值及错误。</summary>
        private static RawSheet ReadSheet(ZipArchive archive, string part, string path, string name, List<string> strings, List<bool> dateStyles, ref int cellCount)
        {
            XElement worksheet = ReadXml(archive, part).Root;
            XNamespace ns = RequireSpreadsheetRoot(worksheet, "worksheet");
            var result = new RawSheet(path, name);
            XElement data = worksheet.Element(ns + "sheetData");
            if (data == null)
                return result;

            int lastRow = 0;
            foreach (XElement row in data.Elements(ns + "row"))
            {
                string rowText = (string)row.Attribute("r");
                int rowNumber = rowText == null ? lastRow + 1 : ReadPositiveNumber(rowText, 1048576, "工作表 " + name + " 的行号");
                if (rowNumber > 1048576 || result.Rows.ContainsKey(rowNumber))
                    throw new InvalidDataException("工作表 " + name + " [A" + rowNumber + "]：行号重复或超出 Excel 范围。");
                lastRow = rowNumber;
                var cells = new SortedDictionary<int, RawCell>();
                result.Rows.Add(rowNumber, cells);
                int lastColumn = 0;
                foreach (XElement cell in row.Elements(ns + "c"))
                {
                    string address = (string)cell.Attribute("r");
                    int column = address == null ? lastColumn + 1 : ReadColumn(address, rowNumber, name);
                    if (column > 16384 || cells.ContainsKey(column))
                        throw new InvalidDataException("工作表 " + name + " [" + (address ?? "A" + rowNumber) + "]：列号重复或超出 Excel 范围。");
                    if (++cellCount > MaximumCells)
                        throw new InvalidDataException("工作簿实际单元格超过 500000 个，请拆分配置表。");
                    lastColumn = column;
                    cells.Add(column, ReadCell(cell, ns, strings, dateStyles));
                }
            }
            return result;
        }

        /// <summary>区分 Excel 存储类型；无法安全转换的内容交给校验层按地址报告。</summary>
        private static RawCell ReadCell(XElement cell, XNamespace ns, List<string> strings, List<bool> dateStyles)
        {
            string type = (string)cell.Attribute("t") ?? "n";
            XElement value = cell.Element(ns + "v");
            bool hasFormula = cell.Element(ns + "f") != null;
            if (hasFormula && (value == null || (value.Value.Length == 0 && type != "str")))
                return new RawCell(string.Empty, "公式缺少已保存的缓存结果，请在 Excel 中重新计算并保存；工具不会计算公式。");

            string text = value == null ? string.Empty : value.Value;
            if (type == "e")
                return new RawCell(text, "Excel 单元格错误：" + text);
            if (type == "d")
                return new RawCell(text, "v1 不支持日期单元格，请明确转换为受支持的文本或数字。");
            if (type == "inlineStr")
                return new RawCell(ReadRichText(cell.Element(ns + "is"), ns));
            if (type == "s")
            {
                int index;
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out index) || index >= strings.Count)
                    return new RawCell(text, "共享字符串索引无效，工作簿可能已损坏。");
                return new RawCell(strings[index]);
            }
            if (type == "str")
                return new RawCell(DecodeText(text));
            if (type == "b")
            {
                if (text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                    return new RawCell("true", null, true);
                if (text == "0" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
                    return new RawCell("false", null, true);
                return new RawCell(text, "Excel 布尔单元格的存储值无效。", true);
            }
            if (type != "n")
                return new RawCell(text, "不支持的 Excel 单元格存储类型：" + type);

            string styleText = (string)cell.Attribute("s");
            int styleIndex = 0;
            if (styleText != null && (!int.TryParse(styleText, NumberStyles.None, CultureInfo.InvariantCulture, out styleIndex) || styleIndex >= dateStyles.Count))
                return new RawCell(text, "单元格样式索引无效，无法安全判断数值是否为日期。");
            if (text.Length > 0 && styleIndex < dateStyles.Count && dateStyles[styleIndex])
                return new RawCell(text, "v1 不支持日期或时间格式的数值单元格。");
            return new RawCell(text);
        }

        /// <summary>读取共享字符串，包括多个富文本片段，但不拼入注音辅助内容。</summary>
        private static List<string> ReadSharedStrings(ZipArchive archive, string workbookPart, Dictionary<string, Relationship> relations)
        {
            var result = new List<string>();
            Relationship relation = FindRelationship(relations, "/sharedStrings", false);
            if (relation == null)
                return result;
            XElement root = ReadXml(archive, ResolvePart(workbookPart, relation)).Root;
            XNamespace ns = RequireSpreadsheetRoot(root, "sst");
            foreach (XElement item in root.Elements(ns + "si"))
                result.Add(ReadRichText(item, ns));
            return result;
        }

        /// <summary>按 cellXfs 索引记录日期样式，阻止将 Excel 日期序号误导出为普通数字。</summary>
        private static List<bool> ReadDateStyles(ZipArchive archive, string workbookPart, Dictionary<string, Relationship> relations)
        {
            var result = new List<bool>();
            Relationship relation = FindRelationship(relations, "/styles", false);
            if (relation == null)
                return result;
            XElement root = ReadXml(archive, ResolvePart(workbookPart, relation)).Root;
            XNamespace ns = RequireSpreadsheetRoot(root, "styleSheet");
            var customFormats = new Dictionary<int, string>();
            XElement formats = root.Element(ns + "numFmts");
            if (formats != null)
            {
                foreach (XElement format in formats.Elements(ns + "numFmt"))
                {
                    int id;
                    if (int.TryParse((string)format.Attribute("numFmtId"), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                        customFormats[id] = (string)format.Attribute("formatCode") ?? string.Empty;
                }
            }
            XElement styles = root.Element(ns + "cellXfs");
            if (styles == null)
                return result;
            foreach (XElement style in styles.Elements(ns + "xf"))
            {
                int id;
                if (!int.TryParse((string)style.Attribute("numFmtId") ?? "0", NumberStyles.None, CultureInfo.InvariantCulture, out id))
                    throw new InvalidDataException("样式中的数字格式编号无效。");
                string format;
                bool isDate = (id >= 14 && id <= 22) || (id >= 27 && id <= 36) || (id >= 45 && id <= 47) || (id >= 50 && id <= 58);
                if (customFormats.TryGetValue(id, out format))
                    isDate = IsDateFormat(format);
                result.Add(isDate);
            }
            return result;
        }

        /// <summary>忽略数字格式中的引号、转义、颜色和条件，仅识别实际日期时间标记。</summary>
        private static bool IsDateFormat(string format)
        {
            bool quoted = false;
            for (int index = 0; index < format.Length; index++)
            {
                char character = char.ToLowerInvariant(format[index]);
                if (character == '"')
                {
                    quoted = !quoted;
                    continue;
                }
                if (quoted)
                    continue;
                if (character == '\\' || character == '_' || character == '*')
                {
                    index++;
                    continue;
                }
                if (character == '[')
                {
                    int closing = format.IndexOf(']', index + 1);
                    if (closing < 0)
                        return false;
                    string bracket = format.Substring(index + 1, closing - index - 1).ToLowerInvariant();
                    if (bracket.Length > 0 && bracket.All(item => item == 'h' || item == 'm' || item == 's'))
                        return true;
                    index = closing;
                    continue;
                }
                if (character == 'y' || character == 'm' || character == 'd' || character == 'h' || character == 's')
                    return true;
            }
            return false;
        }

        /// <summary>按原始 XML 顺序合并普通文本和富文本 run。</summary>
        private static string ReadRichText(XElement container, XNamespace ns)
        {
            if (container == null)
                return string.Empty;
            var builder = new StringBuilder();
            foreach (XElement child in container.Elements())
            {
                if (child.Name == ns + "t")
                    builder.Append(child.Value);
                else if (child.Name == ns + "r")
                    foreach (XElement text in child.Elements(ns + "t"))
                        builder.Append(text.Value);
            }
            return DecodeText(builder.ToString());
        }

        /// <summary>单次解码 OOXML 字符转义，避免把经过转义的字面 _xNNNN_ 二次解码。</summary>
        private static string DecodeText(string text)
        {
            return EscapedCharacter.Replace(text, match => ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
        }

        /// <summary>验证 XML 根元素和 OOXML 命名空间，兼容 Transitional 与 Strict 工作簿。</summary>
        private static XNamespace RequireSpreadsheetRoot(XElement root, string expectedName)
        {
            if (root == null || root.Name.LocalName != expectedName || (root.Name.NamespaceName != SpreadsheetNamespace && root.Name.NamespaceName != StrictSpreadsheetNamespace))
                throw new InvalidDataException("缺少或不支持的 SpreadsheetML 元素：" + expectedName);
            return root.Name.Namespace;
        }

        /// <summary>以禁用 DTD 和外部实体的读取器加载一个受尺寸约束的 XML 部件。</summary>
        private static XDocument ReadXml(ZipArchive archive, string part)
        {
            ZipArchiveEntry entry = archive.GetEntry(part);
            if (entry == null)
                throw new InvalidDataException("缺少必需的工作簿部件：" + part);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumPartBytes,
                IgnoreComments = true
            };
            using (Stream stream = entry.Open())
            using (XmlReader reader = XmlReader.Create(stream, settings))
                return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        /// <summary>读取包内关系表；保留外部标记，在引用相关部件时明确拒绝。</summary>
        private static Dictionary<string, Relationship> ReadRelationships(ZipArchive archive, string part)
        {
            int slash = part.LastIndexOf('/');
            string relationsPart = part.Length == 0 ? "_rels/.rels" : part.Substring(0, slash + 1) + "_rels/" + part.Substring(slash + 1) + ".rels";
            XElement root = ReadXml(archive, relationsPart).Root;
            if (root == null || root.Name.LocalName != "Relationships")
                throw new InvalidDataException("包关系表格式无效：" + relationsPart);
            var result = new Dictionary<string, Relationship>(StringComparer.Ordinal);
            foreach (XElement element in root.Elements())
            {
                if (element.Name.LocalName != "Relationship")
                    continue;
                string id = (string)element.Attribute("Id");
                if (string.IsNullOrEmpty(id) || result.ContainsKey(id))
                    throw new InvalidDataException("包关系标识为空或重复：" + relationsPart);
                result.Add(id, new Relationship
                {
                    Target = (string)element.Attribute("Target") ?? string.Empty,
                    Type = (string)element.Attribute("Type") ?? string.Empty,
                    External = string.Equals((string)element.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)
                });
            }
            return result;
        }

        /// <summary>查找唯一的指定类型关系，防止多份主部件造成读取歧义。</summary>
        private static Relationship FindRelationship(Dictionary<string, Relationship> relations, string typeSuffix, bool required)
        {
            Relationship found = null;
            foreach (Relationship relation in relations.Values)
            {
                if (!relation.Type.EndsWith(typeSuffix, StringComparison.Ordinal))
                    continue;
                if (found != null)
                    throw new InvalidDataException("包中存在重复关系类型：" + typeSuffix);
                found = relation;
            }
            if (required && found == null)
                throw new InvalidDataException("找不到工作簿入口关系。");
            return found;
        }

        /// <summary>仅解析包内部件路径；不访问网络，也不将 ZIP 内容解压至文件系统。</summary>
        private static string ResolvePart(string sourcePart, Relationship relation)
        {
            string target = relation.Target;
            if (relation.External || string.IsNullOrEmpty(target) || target.IndexOfAny(new[] { '\\', ':', '?', '#', '\0' }) >= 0)
                throw new InvalidDataException("工作簿引用了不支持的外部或无效部件路径。");
            try
            {
                target = Uri.UnescapeDataString(target);
            }
            catch (UriFormatException exception)
            {
                throw new InvalidDataException("工作簿部件路径转义无效。", exception);
            }
            if (target.IndexOfAny(new[] { '\\', ':', '?', '#', '\0' }) >= 0)
                throw new InvalidDataException("工作簿部件路径包含无效字符。");
            int slash = sourcePart.LastIndexOf('/');
            string combined = target.StartsWith("/", StringComparison.Ordinal) ? target.Substring(1) : sourcePart.Substring(0, slash + 1) + target;
            var segments = new List<string>();
            foreach (string segment in combined.Split('/'))
            {
                if (segment == "." || segment.Length == 0)
                    continue;
                if (segment == "..")
                {
                    if (segments.Count == 0)
                        throw new InvalidDataException("工作簿部件路径越出压缩包根目录。");
                    segments.RemoveAt(segments.Count - 1);
                }
                else
                    segments.Add(segment);
            }
            if (segments.Count == 0)
                throw new InvalidDataException("工作簿部件路径为空。");
            return string.Join("/", segments);
        }

        /// <summary>解析标准 A1 地址并验证其中的行号与包含它的 row 元素一致。</summary>
        private static int ReadColumn(string address, int expectedRow, string sheet)
        {
            int column = 0;
            int index = 0;
            while (index < address.Length && address[index] >= 'A' && address[index] <= 'Z')
            {
                column = column * 26 + address[index] - 'A' + 1;
                if (column > 16384)
                    break;
                index++;
            }
            int row;
            if (column < 1 || column > 16384 || !int.TryParse(address.Substring(index), NumberStyles.None, CultureInfo.InvariantCulture, out row) || row != expectedRow)
                throw new InvalidDataException("工作表 " + sheet + " [" + address + "]：单元格地址无效或与所在行不一致。");
            return column;
        }

        /// <summary>解析 OOXML 正整数属性，并限制在 Excel 支持的地址范围内。</summary>
        private static int ReadPositiveNumber(string text, int maximum, string description)
        {
            int number;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 1 || number > maximum)
                throw new InvalidDataException(description + "无效：" + text);
            return number;
        }

        /// <summary>保存包关系的必要字段，不创建外部连接或延迟加载对象。</summary>
        private sealed class Relationship
        {
            internal string Target;
            internal string Type;
            internal bool External;
        }
    }
}
