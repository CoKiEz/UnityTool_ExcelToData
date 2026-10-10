using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ExcelDataTool.Editor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace ExcelDataTool.Tests.Editor
{
    /// <summary>补充迁移交付所需的确定性、文本转义、错误定位和受限 OOXML 安全边界测试。</summary>
    /// <remarks>夹具来自 Tests/Fixtures；所有修改和导出限定在工程 Temp 下的随机目录。</remarks>
    public sealed class StageThreeTests
    {
        private const string JsonDirectory = "Assets/Generated/Json";
        private const string CodeDirectory = "Assets/Generated/Code";
        private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private string testRoot;
        private string workbookPath;

        /// <summary>建立仅属于本项测试的工程目录并复制固定工作簿。</summary>
        [SetUp]
        public void SetUp()
        {
            testRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ExcelDataToolStageThree", Guid.NewGuid().ToString("N")));
            string inputs = Path.Combine(testRoot, "Assets/Input");
            Directory.CreateDirectory(inputs);
            workbookPath = Path.Combine(inputs, "Enemy.xlsx");
            File.Copy(TestWorkbooks.GetPath("Enemy.xlsx"), workbookPath);
        }

        /// <summary>核实清理目标位于当前工程的本测试范围内后删除临时副本。</summary>
        [TearDown]
        public void TearDown()
        {
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ExcelDataToolStageThree")) + Path.DirectorySeparatorChar;
            if (testRoot != null && testRoot.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        /// <summary>在逗号小数和阿拉伯区域中，五种类型的 JSON 数值及生成文本必须保持完全一致。</summary>
        /// <param name="cultureName">本次模拟的电脑区域文化名称。</param>
        [TestCase("fr-FR")]
        [TestCase("ar-SA")]
        public void CultureSwitch_DoesNotChangeJsonOrGeneratedCode(string cultureName)
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                ValidatedTable initial = RequireValid(CreateSheet());
                string json = TableExporter.GenerateJson(initial);
                string rowCode = TableExporter.GenerateRowCode(initial);
                string tableCode = TableExporter.GenerateTableCode(initial);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                ValidatedTable localized = RequireValid(CreateSheet());
                Assert.That(TableExporter.GenerateJson(localized), Is.EqualTo(json));
                Assert.That(TableExporter.GenerateRowCode(localized), Is.EqualTo(rowCode));
                Assert.That(TableExporter.GenerateTableCode(localized), Is.EqualTo(tableCode));
                JObject row = (JObject)JObject.Parse(json)["rows"][0];
                Assert.That((float)row["speed"], Is.EqualTo(3.5f));
                Assert.That((double)row["weight"], Is.EqualTo(1234.567890123d));
                Assert.That(row["enabled"].Type, Is.EqualTo(JTokenType.Boolean));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        /// <summary>JSON 回读应无损还原引号、反斜杠、控制字符、中文和空字符串。</summary>
        [Test]
        public void JsonEscaping_RoundTripsSpecialTextAndEmptyString()
        {
            const string special = "中文\"引号\\路径\n换行\r回车\t制表\0零字符\u2028分隔符😀";
            RawSheet sheet = CreateSheet();
            SetCell(sheet, 4, 2, special);
            sheet.Rows[5] = new SortedDictionary<int, RawCell>(sheet.Rows[4]);
            SetCell(sheet, 5, 1, "1002");
            SetCell(sheet, 5, 2, string.Empty);
            ValidatedTable table = RequireValid(sheet);
            TableExporter.Export(table, testRoot, JsonDirectory, CodeDirectory);
            byte[] bytes = File.ReadAllBytes(Path.Combine(testRoot, JsonDirectory, "Boundary.json"));
            string text = new UTF8Encoding(false, true).GetString(bytes);
            JObject json = JObject.Parse(text);
            Assert.That((string)json["rows"][0]["name"], Is.EqualTo(special));
            Assert.That((string)json["rows"][1]["name"], Is.EqualTo(string.Empty));
            StringAssert.Contains("中文", text);
            StringAssert.DoesNotContain(testRoot, text);
            Assert.That(bytes.Take(3), Is.Not.EqualTo(new byte[] { 0xef, 0xbb, 0xbf }), "导出不添加 UTF-8 BOM。");
        }

        /// <summary>第三行说明中的 XML 标记和换行不能关闭文档注释并注入 C# 语句。</summary>
        [Test]
        public void Descriptions_CannotEscapeGeneratedDocumentation()
        {
            RawSheet sheet = CreateSheet();
            SetCell(sheet, 3, 2, "</summary>\npublic class Injected {}\u2028& \"说明\" '内容'");
            string code = TableExporter.GenerateRowCode(RequireValid(sheet));
            StringAssert.Contains("&lt;/summary&gt;", code);
            StringAssert.Contains("&amp; &quot;说明&quot; &apos;内容&apos;", code);
            StringAssert.DoesNotContain("\npublic class Injected", code);
            StringAssert.DoesNotContain("\u2028", code);
            string injectedLine = code.Split('\n').Single(line => line.Contains("public class Injected"));
            Assert.That(injectedLine.TrimStart().StartsWith("///", StringComparison.Ordinal), Is.True);
        }

        /// <summary>多个独立错误应在一次校验中全部返回，并准确保留源文件、工作表及行列。</summary>
        [Test]
        public void MultipleErrors_PreserveEverySourceCoordinate()
        {
            RawSheet sheet = CreateSheet();
            SetCell(sheet, 1, 2, "class");
            SetCell(sheet, 2, 3, "decimal");
            SetCell(sheet, 4, 4, "1,5");
            sheet.Rows[5] = new SortedDictionary<int, RawCell>(sheet.Rows[4]);
            SetCell(sheet, 5, 4, "1.5");
            SetCell(sheet, 5, 5, "不是布尔值");
            ValidationResult result = TableValidator.Validate(sheet);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Table, Is.Null);
            foreach (string address in new[] { "B1", "C2", "D4", "E5", "A5" })
                Assert.That(result.Issues.Any(issue => issue.ToString().Contains(" / " + address + ": ")), Is.True, address);
            Assert.That(result.Issues.All(issue => issue.SourcePath == workbookPath && issue.SheetName == "Boundary"), Is.True);
        }

        /// <summary>浮点类型与整数一样必须提供数据，不能把空值默认补成零。</summary>
        /// <param name="type">要求必填的浮点字段类型。</param>
        [TestCase("float")]
        [TestCase("double")]
        public void EmptyFloatingPointValue_IsRejectedAtActualCell(string type)
        {
            RawSheet sheet = CreateSheet();
            SetCell(sheet, 2, 3, type);
            SetCell(sheet, 4, 3, string.Empty);
            ValidationResult result = TableValidator.Validate(sheet);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues.Any(issue => issue.Row == 4 && issue.Column == 3), Is.True);
        }

        /// <summary>int 范围两端的整数必须精确还原，包括 Excel 可能保存的科学记数法。</summary>
        [Test]
        public void IntegerRangeEndpoints_RemainExact()
        {
            RawSheet sheet = CreateSheet();
            SetCell(sheet, 4, 1, "-2147483648");
            sheet.Rows[5] = new SortedDictionary<int, RawCell>(sheet.Rows[4]);
            SetCell(sheet, 5, 1, "2.147483647e9");
            ValidatedTable table = RequireValid(sheet);
            Assert.That(table.Rows[0][0], Is.EqualTo(int.MinValue));
            Assert.That(table.Rows[1][0], Is.EqualTo(int.MaxValue));
            JObject json = JObject.Parse(TableExporter.GenerateJson(table));
            Assert.That(json["rows"][0]["id"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That((int)json["rows"][1]["id"], Is.EqualTo(int.MaxValue));
        }

        /// <summary>真实 XLSX 中的日期和 Excel 错误格不能被当作普通字符串悄悄导出。</summary>
        /// <param name="storageType">OOXML 单元格存储类型。</param>
        /// <param name="value">日期或错误值。</param>
        /// <param name="reason">预期出现在定位错误中的原因。</param>
        [TestCase("d", "2026-10-10T00:00:00", "日期")]
        [TestCase("e", "#DIV/0!", "#DIV/0!")]
        public void UnsupportedStoredCell_ReportsPreciseLocation(string storageType, string value, string reason)
        {
            RewritePart("xl/worksheets/sheet1.xml", xml =>
            {
                XDocument document = XDocument.Parse(xml);
                XElement cell = FindCell(document, "B4");
                cell.SetAttributeValue("t", storageType);
                cell.ReplaceNodes(new XElement(Spreadsheet + "v", value));
                return document.ToString();
            });
            ValidationResult result = TableValidator.Validate(XlsxWorkbookReader.Read(workbookPath).Sheets.Single(sheet => sheet.Name == "Enemy"));
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues.Any(issue => issue.Row == 4 && issue.Column == 2 && issue.Message.Contains(reason)), Is.True);
        }

        /// <summary>公式确实缓存了空字符串时应合法，区别于没有缓存的公式。</summary>
        [Test]
        public void FormulaWithCachedEmptyString_RemainsValid()
        {
            RewritePart("xl/worksheets/sheet1.xml", xml =>
            {
                XDocument document = XDocument.Parse(xml);
                XElement cell = FindCell(document, "B4");
                cell.SetAttributeValue("t", "str");
                cell.ReplaceNodes(new XElement(Spreadsheet + "f", "IF(1,\"\",\"x\")"), new XElement(Spreadsheet + "v"));
                return document.ToString();
            });
            ValidatedTable table = RequireValid(XlsxWorkbookReader.Read(workbookPath).Sheets.Single(sheet => sheet.Name == "Enemy"));
            Assert.That(table.Rows[0][1], Is.EqualTo(string.Empty));
        }

        /// <summary>XML 带有 DTD 时应在读取阶段拒绝，不展开用户提供的实体。</summary>
        [Test]
        public void XmlDocumentType_IsRejectedBeforeEntityExpansion()
        {
            RewritePart("xl/worksheets/sheet1.xml", xml =>
            {
                XDocument document = XDocument.Parse(xml);
                document.AddFirst(new XDocumentType("worksheet", null, null, "<!ENTITY injected '不应展开'>"));
                return document.ToString();
            });
            Assert.Throws<InvalidDataException>(() => XlsxWorkbookReader.Read(workbookPath));
        }

        /// <summary>工作表关系引用外部文件时应明确拒绝，不访问外部路径。</summary>
        [Test]
        public void ExternalWorksheetRelationship_IsRejected()
        {
            string dataSheetRelationship;
            using (var archive = ZipFile.OpenRead(workbookPath))
            using (Stream stream = archive.GetEntry("xl/workbook.xml").Open())
            {
                XElement sheet = XDocument.Load(stream).Descendants(Spreadsheet + "sheet")
                    .Single(item => (string)item.Attribute("name") == "Enemy");
                dataSheetRelationship = sheet.Attributes().Single(attribute => attribute.Name.LocalName == "id").Value;
            }
            RewritePart("xl/_rels/workbook.xml.rels", xml =>
            {
                XDocument document = XDocument.Parse(xml);
                // 关系部件中的顺序不代表数据表顺序；#说明 页按协议跳过，不适合作为拒绝断言的目标。
                XElement relationship = document.Root.Elements().Single(item => (string)item.Attribute("Id") == dataSheetRelationship);
                relationship.SetAttributeValue("TargetMode", "External");
                relationship.SetAttributeValue("Target", "file:///outside-project/secret.xml");
                return document.ToString();
            });
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => XlsxWorkbookReader.Read(workbookPath));
            StringAssert.Contains("外部", exception.Message);
        }

        /// <summary>运行时生成物不能写入 Editor 或系统保留文件名目录，失败不应留下任何输出。</summary>
        /// <param name="directory">在写入前必须拒绝的工程内目标目录。</param>
        [TestCase("Assets/Editor/Generated")]
        [TestCase("Assets/CON")]
        public void UnsafeRuntimeOutputLocation_IsRejectedBeforeWriting(string directory)
        {
            Assert.Throws<ArgumentException>(() => TableExporter.Export(RequireValid(CreateSheet()), testRoot, directory, CodeDirectory));
            Assert.That(Directory.Exists(Path.Combine(testRoot, CodeDirectory)), Is.False);
            Assert.That(Directory.GetFiles(Path.Combine(testRoot, "Assets"), "*.json", SearchOption.AllDirectories), Is.Empty);
        }

        /// <summary>建立包含全部五种基础类型的最小有效表，不依赖生成类或运行时示例。</summary>
        private RawSheet CreateSheet()
        {
            var sheet = new RawSheet(workbookPath, "Boundary");
            string[][] rows =
            {
                new[] { "id", "name", "speed", "weight", "enabled" },
                new[] { "int", "string", "float", "double", "bool" },
                new[] { "编号", "名称", "速度", "权重", "是否启用" },
                new[] { "1001", "测试中文", "3.5", "1234.567890123", "true" }
            };
            for (int row = 0; row < rows.Length; row++)
                for (int column = 0; column < rows[row].Length; column++)
                    SetCell(sheet, row + 1, column + 1, rows[row][column]);
            return sheet;
        }

        /// <summary>校验合法测试前置条件，避免后续断言只得到空引用错误。</summary>
        private static ValidatedTable RequireValid(RawSheet sheet)
        {
            ValidationResult result = TableValidator.Validate(sheet);
            Assert.That(result.IsValid, Is.True, string.Join("\n", result.Issues.Select(issue => issue.ToString())));
            return result.Table;
        }

        /// <summary>更新稀疏模型中的一个原始单元格，保持 Excel 的一开始坐标。</summary>
        private static void SetCell(RawSheet sheet, int row, int column, string text)
        {
            if (!sheet.Rows.ContainsKey(row)) sheet.Rows[row] = new SortedDictionary<int, RawCell>();
            sheet.Rows[row][column] = new RawCell(text);
        }

        /// <summary>按 A1 地址定位真实工作表 XML 的单元格。</summary>
        private static XElement FindCell(XDocument document, string address)
        {
            return document.Descendants(Spreadsheet + "c").Single(cell => (string)cell.Attribute("r") == address);
        }

        /// <summary>只重建当前测试工作簿中的指定 ZIP 部件，原始固定夹具始终保持只读。</summary>
        private void RewritePart(string partName, Func<string, string> transform)
        {
            string replacement = Path.Combine(testRoot, "replacement-" + Guid.NewGuid().ToString("N") + ".xlsx");
            using (var input = ZipFile.OpenRead(workbookPath))
            using (var output = ZipFile.Open(replacement, ZipArchiveMode.Create))
                foreach (ZipArchiveEntry entry in input.Entries)
                    using (Stream source = entry.Open())
                    using (Stream destination = output.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open())
                    {
                        if (entry.FullName != partName) source.CopyTo(destination);
                        else
                            using (var reader = new StreamReader(source, Encoding.UTF8))
                            using (var writer = new StreamWriter(destination, new UTF8Encoding(false)))
                                writer.Write(transform(reader.ReadToEnd()));
                    }
            File.Copy(replacement, workbookPath, true);
            File.Delete(replacement);
        }
    }
}
