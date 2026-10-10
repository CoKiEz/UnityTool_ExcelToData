using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExcelDataTool.Editor.Core;
using ExcelDataTool.Editor.UI;
using NUnit.Framework;
using UnityEngine;

namespace ExcelDataTool.Tests.Editor
{
    /// <summary>验证第二阶段窗口设置持久化及有界原始数据预览，不访问用户真实设置。</summary>
    public sealed class StageTwoEditorTests
    {
        private string testRoot;

        /// <summary>在当前工程 Temp 内建立独立的设置存储范围。</summary>
        [SetUp]
        public void SetUp()
        {
            testRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ExcelDataToolEditorTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Path.Combine(testRoot, "Assets"));
        }

        /// <summary>仅清理经过前缀检查的本次测试目录。</summary>
        [TearDown]
        public void TearDown()
        {
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ExcelDataToolEditorTests")) + Path.DirectorySeparatorChar;
            if (testRoot != null && testRoot.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        /// <summary>不存在的设置返回稳定默认值，读取本身不创建设置文件。</summary>
        [Test]
        public void MissingSettings_ReturnDefaultsWithoutWriting()
        {
            ExcelDataToolWindowSettings settings = ExcelDataToolSettingsStore.Load(testRoot);
            Assert.That(settings.GeneratedNamespace, Is.EqualTo("ExcelDataTool.Generated"));
            Assert.That(settings.JsonDirectory, Is.EqualTo("Assets/GameData/Generated/Json"));
            Assert.That(settings.CodeDirectory, Is.EqualTo("Assets/GameData/Generated/Code"));
            Assert.That(File.Exists(Path.Combine(testRoot, "UserSettings/ExcelDataTool.json")), Is.False);
        }

        /// <summary>重新加载一个新设置对象后，源、输出、命名空间和当前表选择保持一致。</summary>
        [Test]
        public void SettingsRoundTrip_PersistsInputsOutputsNamespaceAndSelection()
        {
            var settings = new ExcelDataToolWindowSettings
            {
                SourcePaths = new List<string> { "Assets/数据目录", "Assets/Input/Card.xlsx" },
                JsonDirectory = "Assets/Config/Json",
                CodeDirectory = "Assets/Config/Code",
                GeneratedNamespace = "Game.Tables",
                SelectedSourcePath = "Assets/Input/Card.xlsx",
                SelectedSheetName = "Rarity"
            };
            string path = Path.Combine(testRoot, "UserSettings/RoundTrip.json");
            ExcelDataToolSettingsStore.Save(settings, testRoot, path);
            ExcelDataToolWindowSettings loaded = ExcelDataToolSettingsStore.Load(testRoot, path);
            Assert.That(loaded.SourcePaths, Is.EqualTo(settings.SourcePaths));
            Assert.That(loaded.JsonDirectory, Is.EqualTo(settings.JsonDirectory));
            Assert.That(loaded.CodeDirectory, Is.EqualTo(settings.CodeDirectory));
            Assert.That(loaded.GeneratedNamespace, Is.EqualTo(settings.GeneratedNamespace));
            Assert.That(loaded.SelectedSourcePath, Is.EqualTo(settings.SelectedSourcePath));
            Assert.That(loaded.SelectedSheetName, Is.EqualTo(settings.SelectedSheetName));
        }

        /// <summary>两个项目的默认设置文件相互隔离，不能污染当前用户工程。</summary>
        [Test]
        public void Settings_AreScopedToProjectRoot()
        {
            string secondRoot = Path.Combine(testRoot, "AnotherProject");
            Directory.CreateDirectory(Path.Combine(secondRoot, "Assets"));
            ExcelDataToolSettingsStore.Save(new ExcelDataToolWindowSettings { GeneratedNamespace = "First.Config" }, testRoot);
            Assert.That(ExcelDataToolSettingsStore.Load(secondRoot).GeneratedNamespace, Is.EqualTo("ExcelDataTool.Generated"));
            Assert.That(ExcelDataToolSettingsStore.Load(testRoot).GeneratedNamespace, Is.EqualTo("First.Config"));
        }

        /// <summary>自定义设置位置越出所属项目时必须在写入之前拒绝。</summary>
        [Test]
        public void SettingsStore_RejectsOutsideProjectPath()
        {
            string outside = Path.GetFullPath(Path.Combine(testRoot, "../outside-" + Guid.NewGuid().ToString("N") + ".json"));
            Assert.Catch(() => ExcelDataToolSettingsStore.Save(new ExcelDataToolWindowSettings(), testRoot, outside));
            Assert.That(File.Exists(outside), Is.False);
        }

        /// <summary>字段默认值不能把其他工具的 JSON 冒认为本工具设置并覆盖。</summary>
        [Test]
        public void SettingsWithoutExplicitMarker_ArePreserved()
        {
            string path = Path.Combine(testRoot, "UserSettings/Unknown.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            const string original = "{\"unrelated\":true}";
            File.WriteAllText(path, original);
            Assert.Catch(() => ExcelDataToolSettingsStore.Load(testRoot, path));
            Assert.Catch(() => ExcelDataToolSettingsStore.Save(new ExcelDataToolWindowSettings(), testRoot, path));
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }

        /// <summary>大表预览只保留前一百条记录和三十列，仍报告完整数据规模。</summary>
        [Test]
        public void Preview_IsBoundedAndRetainsActualExcelRows()
        {
            var sheet = new RawSheet("Large.xlsx", "Large");
            for (int row = 1; row <= 3; row++)
                for (int column = 1; column <= 45; column++) Set(sheet, row, column, "表头");
            // 行号从 10 开始，检验预览不会把稀疏源表的 Excel 坐标重新编号。
            for (int row = 10; row < 160; row++)
                for (int column = 1; column <= 45; column++) Set(sheet, row, column, row + ":" + column);
            TablePreviewData preview = TablePreviewData.Create(sheet);
            Assert.That(preview.TotalDataRowCount, Is.EqualTo(150));
            Assert.That(preview.ColumnCount, Is.EqualTo(45));
            Assert.That(preview.VisibleColumnCount, Is.EqualTo(30));
            Assert.That(preview.RowNumbers.Count, Is.EqualTo(103));
            Assert.That(preview.RowNumbers.Take(4), Is.EqualTo(new[] { 1, 2, 3, 10 }));
            Assert.That(preview.ContainsCell(109, 30), Is.True);
            Assert.That(preview.ContainsCell(110, 30), Is.False);
            Assert.That(preview.ContainsCell(10, 31), Is.False);
            Assert.That(preview.GetCellText(10, 27), Is.EqualTo("10:27"));
            Assert.That(TablePreviewData.GetCellAddress(7, 27), Is.EqualTo("AA7"));
        }

        /// <summary>错误单元格仍可查看原值，整行空白不会占用有限预览名额。</summary>
        [Test]
        public void Preview_IncludesInvalidValuesAndSkipsEmptyRows()
        {
            var sheet = new RawSheet("Invalid.xlsx", "Invalid");
            Set(sheet, 1, 1, "id");
            Set(sheet, 2, 1, "int");
            Set(sheet, 4, 1, "不是数字");
            Set(sheet, 5, 1, "");
            Set(sheet, 9, 1, "2001");
            TablePreviewData preview = TablePreviewData.Create(sheet);
            Assert.That(preview.TotalDataRowCount, Is.EqualTo(2));
            Assert.That(preview.ContainsCell(4, 1), Is.True);
            Assert.That(preview.GetCellText(4, 1), Is.EqualTo("不是数字"));
            Assert.That(preview.ContainsCell(5, 1), Is.False);
            Assert.That(preview.ContainsCell(9, 1), Is.True);
        }

        /// <summary>为预览模型设置一个有真实行列坐标的单元格。</summary>
        private static void Set(RawSheet sheet, int row, int column, string value)
        {
            if (!sheet.Rows.ContainsKey(row)) sheet.Rows[row] = new SortedDictionary<int, RawCell>();
            sheet.Rows[row][column] = new RawCell(value);
        }
    }
}
