using System;
using System.Collections.Generic;
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
    /// <summary>验证第二阶段多源扫描、逐表失败隔离、增量导出与可配置代码生成。</summary>
    /// <remarks>工作簿修改与导出仅发生在当前工程 Temp 的随机测试目录内。</remarks>
    public sealed class StageTwoTests
    {
        private const string JsonDirectory = "Assets/Generated/Json";
        private const string CodeDirectory = "Assets/Generated/Code";
        private const string DefaultNamespace = "ExcelDataTool.Generated";
        private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private string testRoot;
        private string sourceDirectory;
        private string enemyPath;
        private string cardPath;

        /// <summary>复制两个随测试迁移的固定工作簿到独立目录，不依赖用户的 GameData 或运行时示例。</summary>
        [SetUp]
        public void SetUp()
        {
            testRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ExcelDataToolStageTwo", Guid.NewGuid().ToString("N")));
            sourceDirectory = Path.Combine(testRoot, "Assets/Input");
            Directory.CreateDirectory(sourceDirectory);
            enemyPath = Path.Combine(sourceDirectory, "Enemy.xlsx");
            cardPath = Path.Combine(sourceDirectory, "Card.xlsx");
            File.Copy(TestWorkbooks.GetPath("Enemy.xlsx"), enemyPath);
            File.Copy(TestWorkbooks.GetPath("Card.xlsx"), cardPath);
        }

        /// <summary>核实路径位于本轮测试目录后清理测试副本。</summary>
        [TearDown]
        public void TearDown()
        {
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ExcelDataToolStageTwo")) + Path.DirectorySeparatorChar;
            if (testRoot != null && testRoot.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        /// <summary>目录与显式文件重复选择时只读取一次，并忽略 Excel 临时锁文件和说明页。</summary>
        [Test]
        public void Scan_DeduplicatesSourcesAndFindsEveryDataSheet()
        {
            File.WriteAllText(Path.Combine(sourceDirectory, "~$Enemy.xlsx"), "临时锁文件");
            File.WriteAllText(Path.Combine(sourceDirectory, "notes.txt"), "非工作簿");
            WorkbookScanResult scan = WorkbookScanner.Scan(testRoot, new[] { "Assets/Input", enemyPath, "Assets/Input/./Enemy.xlsx" });
            Assert.That(scan.SourceFiles.Count, Is.EqualTo(2));
            Assert.That(scan.Tables.Select(item => item.Name).OrderBy(name => name).ToArray(), Is.EqualTo(new[] { "Card", "Enemy", "Rarity" }));
            Assert.That(scan.Tables.All(item => item.IsValid), Is.True);
            Assert.That(scan.Tables.Single(item => item.Name == "Card").Table.Rows.Count, Is.EqualTo(3));
        }

        /// <summary>递归目录中的工作簿可被发现，且多文件多表一次写出完整 JSON 与 C#。</summary>
        [Test]
        public void Batch_ExportsAllThreeTablesFromNestedDirectories()
        {
            string nested = Path.Combine(sourceDirectory, "卡牌");
            Directory.CreateDirectory(nested);
            File.Move(cardPath, Path.Combine(nested, "Card.xlsx"));
            BatchExportResult result = ExportAll();
            Assert.That(result.SuccessCount, Is.EqualTo(3));
            Assert.That(result.FailureCount, Is.Zero);
            Assert.That(result.WrittenCount, Is.EqualTo(9));
            Assert.That(result.SkippedCount, Is.Zero);
            JToken first = JObject.Parse(File.ReadAllText(JsonPath("Card")))["rows"][0];
            Assert.That((string)first["name"], Is.EqualTo("火球术"));
            Assert.That((double)first["weight"], Is.EqualTo(0.125d));
            Assert.That(File.Exists(CodePath("Rarity", "Table")), Is.True);
        }

        /// <summary>完全相同的内容不能改写任何数据、代码或归属记录，时间戳也必须保持。</summary>
        [Test]
        public void UnchangedBatch_PreservesAllFileTimestamps()
        {
            ExportAll();
            Dictionary<string, byte[]> snapshot = SnapshotOutputs();
            DateTime sentinel = Stamp(snapshot.Keys);
            BatchExportResult result = ExportAll();
            Assert.That(result.SuccessCount, Is.EqualTo(3));
            Assert.That(result.WrittenCount, Is.Zero);
            Assert.That(result.SkippedCount, Is.EqualTo(9));
            AssertUnchanged(snapshot, sentinel);
        }

        /// <summary>只修改一个数值时仅写入对应 JSON，其他八个产物与其他表记录保持原时间戳。</summary>
        [Test]
        public void ValueEdit_UpdatesOnlyAffectedJson()
        {
            ExportAll();
            Dictionary<string, byte[]> snapshot = SnapshotOutputs();
            DateTime sentinel = Stamp(snapshot.Keys);
            RewriteXml(enemyPath, "xl/worksheets/sheet1.xml", doc => SetCell(doc, "C4", "130"));
            BatchExportResult result = ExportAll();
            Assert.That(result.WrittenCount, Is.EqualTo(1));
            Assert.That(result.SkippedCount, Is.EqualTo(8));
            Assert.That((int)JObject.Parse(File.ReadAllText(JsonPath("Enemy")))["rows"][0]["maxHealth"], Is.EqualTo(130));
            Assert.That(snapshot.Remove(JsonPath("Enemy")), Is.True);
            Assert.That(snapshot.Remove(RecordPath("Enemy")), Is.True);
            AssertUnchanged(snapshot, sentinel);
        }

        /// <summary>字段类型变动重建行类，但不改写仍相同的容器类或其他表。</summary>
        [Test]
        public void SchemaEdit_RewritesRowClassAndPreservesContainer()
        {
            ExportAll();
            DateTime sentinel = Stamp(new[] { CodePath("Card", "Table"), CodePath("Enemy", "Row") });
            RewriteXml(cardPath, "xl/worksheets/sheet1.xml", doc => SetCell(doc, "D2", "double", true));
            BatchExportResult result = ExportAll();
            Assert.That(result.FailureCount, Is.Zero);
            StringAssert.Contains("public double power;", File.ReadAllText(CodePath("Card", "Row")));
            Assert.That(File.GetLastWriteTimeUtc(CodePath("Card", "Table")), Is.EqualTo(sentinel));
            Assert.That(File.GetLastWriteTimeUtc(CodePath("Enemy", "Row")), Is.EqualTo(sentinel));
        }

        /// <summary>Git 换行转换不应导致无变化代码重新写入或失去归属。</summary>
        [Test]
        public void CrLfFiles_AreSkippedWithoutRewriting()
        {
            ExportAll();
            string rowPath = CodePath("Enemy", "Row");
            File.WriteAllText(rowPath, File.ReadAllText(rowPath).Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            byte[] bytes = File.ReadAllBytes(rowPath);
            DateTime sentinel = Stamp(new[] { rowPath });
            BatchExportResult result = ExportAll();
            Assert.That(result.WrittenCount, Is.Zero);
            Assert.That(result.SkippedCount, Is.EqualTo(9));
            Assert.That(File.ReadAllBytes(rowPath), Is.EqualTo(bytes));
            Assert.That(File.GetLastWriteTimeUtc(rowPath), Is.EqualTo(sentinel));
        }

        /// <summary>跨文件同名表双方都失败，其他合法表继续；已经导出的同名结果不改变。</summary>
        [Test]
        public void DuplicateNames_BlockBothTablesBeforeAnyOverwrite()
        {
            ExportAll();
            byte[] previous = File.ReadAllBytes(JsonPath("Enemy"));
            File.Copy(enemyPath, Path.Combine(sourceDirectory, "EnemyCopy.xlsx"));
            WorkbookScanResult scan = Scan();
            ScannedTable[] conflicts = scan.Tables.Where(item => item.Name == "Enemy").ToArray();
            Assert.That(conflicts.Length, Is.EqualTo(2));
            Assert.That(conflicts.All(item => !item.IsValid && item.Issues.Any(issue => issue.Message.Contains("冲突") || issue.Message.Contains("重复"))), Is.True);
            BatchExportResult result = ExportAll(scan);
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.FailureCount, Is.EqualTo(2));
            Assert.That(File.ReadAllBytes(JsonPath("Enemy")), Is.EqualTo(previous));
            BatchExportResult single = BatchExportService.Export(scan, testRoot, JsonDirectory, CodeDirectory, DefaultNamespace, enemyPath, "Enemy");
            Assert.That(single.SuccessCount, Is.Zero, "当前表导出也不能绕过全局名称冲突。");
            Assert.That(single.FailureCount, Is.EqualTo(1));
        }

        /// <summary>重复 ID 与损坏工作簿分别定位并隔离，合法的 Card/Rarity 仍可以导出。</summary>
        [Test]
        public void InvalidAndDamagedInputs_PreserveOldDataAndAllowOtherTables()
        {
            ExportAll();
            byte[] previous = File.ReadAllBytes(JsonPath("Enemy"));
            RewriteXml(enemyPath, "xl/worksheets/sheet1.xml", doc => SetCell(doc, "A5", "1001"));
            File.WriteAllText(Path.Combine(sourceDirectory, "Broken.xlsx"), "损坏内容");
            WorkbookScanResult scan = Scan();
            Assert.That(scan.Tables.Single(item => item.Name == "Enemy").Issues.Any(issue => issue.Row == 5 && issue.Column == 1), Is.True);
            BatchExportResult result = ExportAll(scan);
            Assert.That(result.FailureCount, Is.EqualTo(2));
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(File.ReadAllBytes(JsonPath("Enemy")), Is.EqualTo(previous));
        }

        /// <summary>非法工作表名不导致其他工作表被遗漏，错误保留明确表名。</summary>
        [Test]
        public void InvalidSheetName_DoesNotStopSiblingSheet()
        {
            RenameFirstSheet(cardPath, "card-invalid");
            BatchExportResult result = ExportAll();
            Assert.That(result.FailureCount, Is.EqualTo(1));
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(result.Tables.Single(item => item.Name == "card-invalid").Issues.Count, Is.GreaterThan(0));
            Assert.That(File.Exists(JsonPath("Rarity")), Is.True);
        }

        /// <summary>丢失目录显示错误并允许其余已指定源文件继续工作。</summary>
        [Test]
        public void MissingSource_IsReportedAlongsideValidSources()
        {
            WorkbookScanResult scan = WorkbookScanner.Scan(testRoot, new[] { enemyPath, "Assets/MissingDirectory" });
            BatchExportResult result = ExportAll(scan);
            Assert.That(result.FailureCount, Is.EqualTo(1));
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.Tables.Where(item => !item.Success).All(item => item.Issues.Count > 0), Is.True);
        }

        /// <summary>已有手写脚本保护发生于该表的任何写入之前，批量中的其他表仍导出。</summary>
        [Test]
        public void UnownedCode_BlocksOnlyConflictingTable()
        {
            string target = CodePath("Card", "Row");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, "// 用户手写配置，禁止覆盖");
            BatchExportResult result = ExportAll();
            Assert.That(result.FailureCount, Is.EqualTo(1));
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(File.Exists(JsonPath("Card")), Is.False);
            Assert.That(File.ReadAllText(target), Is.EqualTo("// 用户手写配置，禁止覆盖"));
        }

        /// <summary>即使内容与目标相同，缺少归属记录也不能被当作无变化成功跳过。</summary>
        [Test]
        public void IdenticalUnownedFiles_AreStillRejected()
        {
            ExportAll();
            File.Delete(RecordPath("Enemy"));
            byte[] previous = File.ReadAllBytes(JsonPath("Enemy"));
            BatchExportResult result = ExportAll();
            Assert.That(result.FailureCount, Is.EqualTo(1));
            Assert.That(result.SuccessCount, Is.EqualTo(2));
            Assert.That(File.ReadAllBytes(JsonPath("Enemy")), Is.EqualTo(previous));
            Assert.That(File.Exists(RecordPath("Enemy")), Is.False);
        }

        /// <summary>当前表导出只写选中的表；原选择消失后必须明确失败。</summary>
        [Test]
        public void CurrentSelection_ExportsOnlyThatTableAndRejectsMissingSelection()
        {
            BatchExportResult result = BatchExportService.Export(Scan(), testRoot, JsonDirectory, CodeDirectory, DefaultNamespace, cardPath, "Rarity");
            Assert.That(result.SuccessCount, Is.EqualTo(1));
            Assert.That(result.WrittenCount, Is.EqualTo(3));
            Assert.That(File.Exists(JsonPath("Card")), Is.False);
            BatchExportResult missing = BatchExportService.Export(Scan(), testRoot, JsonDirectory, CodeDirectory, DefaultNamespace, cardPath, "Missing");
            Assert.That(missing.SuccessCount, Is.Zero);
            Assert.That(missing.FailureCount, Is.EqualTo(1));
        }

        /// <summary>合法命名空间参与生成；再次使用相同设置应跳过全部文件。</summary>
        [Test]
        public void NamespaceSetting_ChangesCodeAndThenBecomesIncremental()
        {
            ExportAll();
            BatchExportResult changed = ExportAll(null, "MyGame.Config");
            Assert.That(changed.SuccessCount, Is.EqualTo(3));
            Assert.That(changed.WrittenCount, Is.EqualTo(6));
            Assert.That(changed.SkippedCount, Is.EqualTo(3));
            StringAssert.Contains("namespace MyGame.Config", File.ReadAllText(CodePath("Card", "Row")));
            Assert.That(ExportAll(null, "MyGame.Config").WrittenCount, Is.Zero);
        }

        /// <summary>空值、关键字和代码注入形式的命名空间在任何文件写入前被拒绝。</summary>
        /// <param name="invalidNamespace">不合法的命名空间配置。</param>
        [TestCase("")]
        [TestCase("Game..Data")]
        [TestCase("Game.class")]
        [TestCase("Game; public class Injected {}")]
        public void InvalidNamespace_DoesNotCreateAnyOutput(string invalidNamespace)
        {
            ValidatedTable table = Scan().Tables.Single(item => item.Name == "Enemy").Table;
            Assert.Catch(() => TableExporter.ExportDetailed(table, testRoot, JsonDirectory, CodeDirectory, invalidNamespace));
            Assert.That(File.Exists(JsonPath("Enemy")), Is.False);
            Assert.That(File.Exists(CodePath("Enemy", "Row")), Is.False);
        }

        /// <summary>源表改名时保留旧输出，并提示由当前源文件留下的过时产物。</summary>
        [Test]
        public void RenamedSheet_WarnsAndKeepsOldOutputs()
        {
            ExportAll();
            byte[] previous = File.ReadAllBytes(JsonPath("Enemy"));
            RenameFirstSheet(enemyPath, "Monster");
            BatchExportResult result = ExportAll();
            Assert.That(result.Warnings.Any(item => item.Contains("Enemy")), Is.True);
            Assert.That(File.ReadAllBytes(JsonPath("Enemy")), Is.EqualTo(previous));
            Assert.That(File.Exists(JsonPath("Monster")), Is.True);
        }

        /// <summary>后续输出被锁定时回滚已替换代码，修复文件占用后可以重试成功。</summary>
        [Test]
        public void LockedOutput_RollsBackAndSupportsRetry()
        {
            ExportAll();
            ValidatedTable enemy = Scan().Tables.Single(item => item.Name == "Enemy").Table;
            var previous = new[] { JsonPath("Enemy"), CodePath("Enemy", "Row"), CodePath("Enemy", "Table"), RecordPath("Enemy") }
                .ToDictionary(path => path, File.ReadAllBytes);
            using (var locked = new FileStream(CodePath("Enemy", "Table"), FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Catch(() => TableExporter.ExportDetailed(enemy, testRoot, JsonDirectory, CodeDirectory, "Retry.Config"));
            foreach (var file in previous)
                Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value));
            Assert.That(TableExporter.ExportDetailed(enemy, testRoot, JsonDirectory, CodeDirectory, "Retry.Config").WrittenCount, Is.EqualTo(2));
        }

        /// <summary>扫描当前测试源目录，使用实际的只读解析和校验路径。</summary>
        private WorkbookScanResult Scan() => WorkbookScanner.Scan(testRoot, new[] { sourceDirectory });

        /// <summary>使用给定扫描结果或重新扫描后执行批量导出。</summary>
        private BatchExportResult ExportAll(WorkbookScanResult scan = null, string generatedNamespace = DefaultNamespace)
            => BatchExportService.Export(scan ?? Scan(), testRoot, JsonDirectory, CodeDirectory, generatedNamespace);

        /// <summary>取得某张表的 JSON 输出位置。</summary>
        private string JsonPath(string table) => Path.GetFullPath(Path.Combine(testRoot, JsonDirectory, table + ".json"));

        /// <summary>取得某张表的行类或容器类输出位置。</summary>
        private string CodePath(string table, string suffix) => Path.GetFullPath(Path.Combine(testRoot, CodeDirectory, table + suffix + ".cs"));

        /// <summary>取得某张表的归属记录位置。</summary>
        private string RecordPath(string table) => Path.GetFullPath(Path.Combine(testRoot, "Assets/ExcelDataTool/ExportRecords", table + ".export.json"));

        /// <summary>同时快照所有生成物和归属记录，避免仅检查文件内容而漏掉写入副作用。</summary>
        private Dictionary<string, byte[]> SnapshotOutputs()
        {
            return Directory.GetFiles(Path.Combine(testRoot, "Assets/Generated"), "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(testRoot, "Assets/ExcelDataTool/ExportRecords")))
                .ToDictionary(Path.GetFullPath, File.ReadAllBytes);
        }

        /// <summary>设置一个固定旧时间，不依赖磁盘时间分辨率或阻塞等待判断文件是否被重写。</summary>
        private static DateTime Stamp(IEnumerable<string> paths)
        {
            DateTime sentinel = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
            foreach (string path in paths) File.SetLastWriteTimeUtc(path, sentinel);
            return sentinel;
        }

        /// <summary>检查内容和最后写入时间均未改变。</summary>
        private static void AssertUnchanged(Dictionary<string, byte[]> files, DateTime sentinel)
        {
            foreach (var file in files)
            {
                Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), file.Key);
                Assert.That(File.GetLastWriteTimeUtc(file.Key), Is.EqualTo(sentinel), file.Key);
            }
        }

        /// <summary>修改测试工作簿中第一个数据表的表名。</summary>
        private void RenameFirstSheet(string path, string name)
        {
            RewriteXml(path, "xl/workbook.xml", doc => doc.Descendants(Spreadsheet + "sheet").First().SetAttributeValue("name", name));
        }

        /// <summary>只在测试副本中重建一个 ZIP XML 部件，其余条目原样复制。</summary>
        private void RewriteXml(string path, string part, Action<XDocument> change)
        {
            string temporary = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + ".xlsx");
            using (var input = ZipFile.OpenRead(path))
            using (var output = ZipFile.Open(temporary, ZipArchiveMode.Create))
                foreach (ZipArchiveEntry entry in input.Entries)
                    using (Stream source = entry.Open())
                    using (Stream destination = output.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open())
                    {
                        if (entry.FullName == part)
                        {
                            XDocument document = XDocument.Load(source);
                            change(document);
                            document.Save(destination);
                        }
                        else source.CopyTo(destination);
                    }
            File.Copy(temporary, path, true);
            File.Delete(temporary);
        }

        /// <summary>按 A1 坐标修改数值或内联文本单元格。</summary>
        private static void SetCell(XDocument document, string address, string text, bool inlineText = false)
        {
            XElement cell = document.Descendants(Spreadsheet + "c").Single(item => (string)item.Attribute("r") == address);
            cell.SetAttributeValue("t", inlineText ? "inlineStr" : "n");
            cell.ReplaceNodes(inlineText
                ? new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "t", text))
                : new XElement(Spreadsheet + "v", text));
        }
    }
}
