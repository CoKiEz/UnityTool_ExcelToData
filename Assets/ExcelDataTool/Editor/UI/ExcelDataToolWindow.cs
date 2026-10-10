using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDataTool.Editor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelDataTool.Editor.UI
{
    /// <summary>通过 UI Toolkit 管理多个 Excel 来源、预览和校验工作表，并执行安全的增量导出。</summary>
    /// <remarks>界面不解析表格、不直接写生成物；读取、冲突检查及写入分别交给核心服务。</remarks>
    public sealed class ExcelDataToolWindow : EditorWindow
    {
        [SerializeField] private ExcelDataToolWindowSettings settings = new ExcelDataToolWindowSettings();
        [SerializeField] private bool settingsLoaded;
        [SerializeField] private string resultText = "添加 .xlsx 文件或源目录，然后扫描并校验；也可直接添加示例。";
        [SerializeField] private HelpBoxMessageType resultType = HelpBoxMessageType.Info;
        private WorkbookScanResult scan;
        private readonly List<ScannedTable> tables = new List<ScannedTable>();
        private readonly List<ValidationIssue> displayedIssues = new List<ValidationIssue>();
        private readonly Dictionary<string, string> exportStatuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private ListView sourceList;
        private ListView tableList;
        private ListView issueList;
        private TextField sourceInput;
        private TextField selectedSourceField;
        private TextField detailsField;
        private Label scanLabel;
        private Label issueLabel;
        private HelpBox resultBox;
        private TablePreviewView preview;

        /// <summary>重建窗口控件和已有内存结果，不读取或保存真实设置，也不读取源文件。</summary>
        /// <remarks>此 Unity 回调无参数和返回值；菜单打开及真实窗口获得焦点后才恢复磁盘设置与扫描。</remarks>
        public void CreateGUI()
        {
            settings = settings ?? new ExcelDataToolWindowSettings();
            settings.SourcePaths = settings.SourcePaths ?? new List<string>();
            rootVisualElement.Clear();
            var page = new ScrollView();
            page.style.flexGrow = 1;
            page.style.paddingLeft = 14;
            page.style.paddingRight = 14;
            page.style.paddingTop = 12;
            page.style.paddingBottom = 14;
            rootVisualElement.Add(page);
            var title = new Label("Excel Data Tool");
            title.style.fontSize = 21;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            page.Add(title);
            var subtitle = new Label("配置来源 → 检查数据 → 导出 JSON + C#");
            subtitle.style.marginBottom = 8;
            page.Add(subtitle);

            var sourceSection = new Foldout { text = "1. 配置来源（文件或递归目录）", value = true };
            page.Add(sourceSection);
            var sourceButtons = CreateButtonRow();
            AddButton(sourceButtons, "添加文件…", BrowseFile);
            AddButton(sourceButtons, "添加目录…", BrowseDirectory);
            AddButton(sourceButtons, "添加示例", AddExamples);
            AddButton(sourceButtons, "移除选中来源", RemoveSelectedSource);
            AddButton(sourceButtons, "清空来源", ClearSources);
            sourceSection.Add(sourceButtons);
            var pathRow = CreateButtonRow();
            sourceInput = new TextField("输入路径") { name = "source-path-input" };
            sourceInput.style.flexGrow = 1;
            sourceInput.tooltip = "可粘贴工程相对路径、绝对文件路径或目录路径，再点击添加路径。";
            pathRow.Add(sourceInput);
            AddButton(pathRow, "添加路径", () => AddSource(sourceInput.value, true));
            sourceSection.Add(pathRow);
            sourceList = CreateList(76, 24);
            sourceList.name = "source-list";
            sourceList.itemsSource = settings.SourcePaths;
            sourceList.bindItem = (element, index) =>
            {
                string path = settings.SourcePaths[index];
                ((Label)element).text = path;
                element.tooltip = path;
            };
            sourceSection.Add(sourceList);

            var options = new Foldout { text = "输出设置（保存到当前工程 UserSettings）", value = false };
            page.Add(options);
            options.Add(CreateSettingField("JSON 输出目录", "json-output-directory", settings.JsonDirectory, value => settings.JsonDirectory = value));
            options.Add(CreateSettingField("C# 输出目录", "code-output-directory", settings.CodeDirectory, value => settings.CodeDirectory = value));
            options.Add(CreateSettingField("生成命名空间", "generated-namespace", settings.GeneratedNamespace, value => settings.GeneratedNamespace = value));
            var saveRow = CreateButtonRow();
            AddButton(saveRow, "保存设置", () => { SaveSettings(); ShowResult("设置已保存到 UserSettings/ExcelDataTool.json。", HelpBoxMessageType.Info); });
            options.Add(saveRow);

            var operations = CreateButtonRow();
            AddButton(operations, "扫描并校验", () => ScanSources(true));
            AddButton(operations, "导出当前表", () => ExportTables(false));
            AddButton(operations, "批量导出", () => ExportTables(true));
            page.Add(operations);
            scanLabel = new Label("尚未扫描来源。每次导出会重新扫描全部配置来源并检查重名。");
            scanLabel.style.whiteSpace = WhiteSpace.Normal;
            page.Add(scanLabel);
            tableList = CreateList(122, 26);
            tableList.name = "table-list";
            tableList.itemsSource = tables;
            tableList.bindItem = (element, index) => BindTable((Label)element, tables[index]);
            tableList.selectionChanged += selection =>
            {
                ScannedTable selected = selection.OfType<ScannedTable>().FirstOrDefault();
                if (selected != null) RunAction(() => SelectTable(selected, null, true));
            };
            page.Add(tableList);
            var currentRow = CreateButtonRow();
            selectedSourceField = new TextField("当前源文件") { isReadOnly = true, name = "selected-source-path" };
            selectedSourceField.style.flexGrow = 1;
            currentRow.Add(selectedSourceField);
            AddButton(currentRow, "定位源文件", RevealSelectedSource);
            page.Add(currentRow);

            var previewTitle = new Label("2. 原始数据预览");
            previewTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            page.Add(previewTitle);
            preview = new TablePreviewView { name = "table-preview" };
            page.Add(preview);
            issueLabel = new Label("3. 错误定位（点击一项切换到相应表和单元格）");
            issueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            issueLabel.style.marginTop = 8;
            page.Add(issueLabel);
            issueList = CreateList(92, 25);
            issueList.name = "issue-list";
            issueList.itemsSource = displayedIssues;
            issueList.bindItem = (element, index) =>
            {
                ((Label)element).text = displayedIssues[index].ToString();
                element.tooltip = displayedIssues[index].ToString() + "\n" + displayedIssues[index].SourcePath;
            };
            issueList.selectionChanged += selection =>
            {
                ValidationIssue issue = selection.OfType<ValidationIssue>().FirstOrDefault();
                if (issue != null) RunAction(() => LocateIssue(issue));
            };
            page.Add(issueList);
            resultBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            page.Add(resultBox);
            detailsField = new TextField("完整操作详情") { multiline = true, isReadOnly = true, name = "operation-details" };
            detailsField.style.minHeight = 105;
            detailsField.style.maxHeight = 190;
            page.Add(detailsField);
            var detailsButtons = CreateButtonRow();
            AddButton(detailsButtons, "复制完整详情", () => EditorGUIUtility.systemCopyBuffer = resultText);
            page.Add(detailsButtons);
            RestoreTableSelection();
            ShowResult(resultText, resultType);
        }

        /// <summary>从菜单打开窗口，并加载当前工程的专用用户设置。</summary>
        [MenuItem("Tools/Excel Data Tool")]
        private static void OpenWindow()
        {
            var window = GetWindow<ExcelDataToolWindow>();
            window.titleContent = new GUIContent("Excel Data Tool");
            window.minSize = new Vector2(700, 560);
            window.Show();
            window.RunAction(window.EnsureInitialized);
        }

        /// <summary>真实窗口恢复焦点时重建重编译后丢失的扫描缓存；纯 CreateGUI 测试不会调用此路径。</summary>
        private void OnFocus()
        {
            if (rootVisualElement.childCount > 0) RunAction(EnsureInitialized);
        }

        /// <summary>首次菜单打开读取设置；Domain Reload 后保留选择并重新扫描。</summary>
        private void EnsureInitialized()
        {
            if (!settingsLoaded)
            {
                settings = ExcelDataToolSettingsStore.Load(GetProjectRoot());
                settingsLoaded = true;
                CreateGUI();
            }
            if (scan == null && settings.SourcePaths.Count > 0)
            {
                string priorResult = resultText;
                HelpBoxMessageType priorType = resultType;
                ScanSources(false);
                ShowResult(priorResult, priorType);
            }
        }

        /// <summary>创建固定行高的虚拟化列表，避免来源及错误增多时一次生成所有元素。</summary>
        private static ListView CreateList(float height, float rowHeight)
        {
            var list = new ListView { fixedItemHeight = rowHeight, selectionType = SelectionType.Single };
            list.style.height = height;
            list.style.marginTop = 4;
            list.style.marginBottom = 5;
            list.makeItem = () =>
            {
                var label = new Label();
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.paddingLeft = 5;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                return label;
            };
            return list;
        }

        /// <summary>创建会在确认编辑后保存的设置输入，避免每键一次磁盘写入。</summary>
        private TextField CreateSettingField(string label, string name, string value, Action<string> update)
        {
            var field = new TextField(label) { name = name, value = value, isDelayed = true };
            field.RegisterValueChangedCallback(change => RunAction(() => { update(change.newValue); SaveSettings(); }));
            return field;
        }

        /// <summary>创建可以随窄窗口换行的按钮容器。</summary>
        private static VisualElement CreateButtonRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 5;
            row.style.marginBottom = 5;
            return row;
        }

        /// <summary>添加统一捕获异常的按钮，保证输入及文件错误不会逃出回调。</summary>
        private void AddButton(VisualElement parent, string text, Action action)
        {
            parent.Add(new Button(() => RunAction(action)) { text = text });
        }

        /// <summary>获取当前 Unity 工程根目录，不依赖进程当前工作目录。</summary>
        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        /// <summary>规范化用户输入来源，以便文件和目录的重叠配置能够稳定识别。</summary>
        private static string AbsoluteSourcePath(string source)
        {
            return Path.GetFullPath(Path.IsPathRooted(source) ? source : Path.Combine(GetProjectRoot(), source));
        }

        /// <summary>浏览并追加一个工作簿；重复点击可以添加多个文件。</summary>
        private void BrowseFile()
        {
            string selected = EditorUtility.OpenFilePanel("添加 Excel 工作簿", GetProjectRoot(), "xlsx");
            if (!string.IsNullOrEmpty(selected)) AddSource(selected, true);
        }

        /// <summary>追加需要递归扫描的目录，具体枚举和临时锁文件过滤由扫描器执行。</summary>
        private void BrowseDirectory()
        {
            string selected = EditorUtility.OpenFolderPanel("添加 Excel 源目录（递归扫描）", GetProjectRoot(), string.Empty);
            if (!string.IsNullOrEmpty(selected)) AddSource(selected, true);
        }

        /// <summary>添加交付的 Enemy 和 Card 示例，不修改源表内容。</summary>
        private void AddExamples()
        {
            AddSource("Assets/GameData/Excel/Enemy.xlsx", false);
            AddSource("Assets/GameData/Excel/Card.xlsx", false);
            SaveSettings();
            ScanSources(true);
        }

        /// <summary>追加去重后的来源并持久化；添加后立即扫描，显示路径和内容错误。</summary>
        private void AddSource(string source, bool scanAfter)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("请输入 .xlsx 文件或源目录路径。");
            string absolute = AbsoluteSourcePath(source.Trim().Trim('"'));
            if (!Directory.Exists(absolute) && !string.Equals(Path.GetExtension(absolute), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请选择现有目录或 .xlsx 文件。");
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!settings.SourcePaths.Any(path => string.Equals(AbsoluteSourcePath(path), absolute, comparison)))
            {
                string root = GetProjectRoot() + Path.DirectorySeparatorChar;
                settings.SourcePaths.Add(absolute.StartsWith(root, comparison)
                    ? absolute.Substring(root.Length).Replace('\\', '/') : absolute);
            }
            sourceList.Rebuild();
            if (scanAfter) { SaveSettings(); ScanSources(true); }
        }

        /// <summary>从配置中移除当前来源，不删除磁盘文件。</summary>
        private void RemoveSelectedSource()
        {
            if (sourceList.selectedIndex < 0 || sourceList.selectedIndex >= settings.SourcePaths.Count) return;
            settings.SourcePaths.RemoveAt(sourceList.selectedIndex);
            sourceList.ClearSelection();
            sourceList.Rebuild();
            SaveSettings();
            ScanSources(true);
        }

        /// <summary>清空配置来源及选择，不改变任何源文件或生成文件。</summary>
        private void ClearSources()
        {
            settings.SourcePaths.Clear();
            settings.SelectedSourcePath = string.Empty;
            settings.SelectedSheetName = string.Empty;
            sourceList.Rebuild();
            SaveSettings();
            ScanSources(true);
        }

        /// <summary>保存当前工程的专用设置；只有显式操作和用户编辑会调用。</summary>
        private void SaveSettings()
        {
            ExcelDataToolSettingsStore.Save(settings, GetProjectRoot());
            settingsLoaded = true;
        }

        /// <summary>重新扫描全部来源并呈现错误，在任意当前表导出前也执行此流程。</summary>
        private void ScanSources(bool showSummary)
        {
            scan = WorkbookScanner.Scan(GetProjectRoot(), settings.SourcePaths);
            tables.Clear();
            tables.AddRange(scan.Tables);
            exportStatuses.Clear();
            displayedIssues.Clear();
            displayedIssues.AddRange(tables.SelectMany(table => table.Issues));
            tableList.Rebuild();
            issueList.Rebuild();
            issueLabel.text = "3. 错误定位（" + displayedIssues.Count + " 项，点击定位工作表和单元格）";
            scanLabel.text = "来源文件 " + scan.SourceFiles.Count + " 个 · 工作表 " + tables.Count + " 张 · 校验通过 "
                + tables.Count(table => table.IsValid) + " 张 · 错误 " + displayedIssues.Count + " 项";
            RestoreTableSelection();
            if (showSummary)
            {
                var summary = new StringBuilder(scanLabel.text);
                foreach (string warning in scan.Warnings) summary.Append("\n提示：").Append(warning);
                foreach (ValidationIssue issue in displayedIssues) summary.Append('\n').Append(issue).Append("\n源文件：").Append(issue.SourcePath);
                ShowResult(summary.ToString(), displayedIssues.Count > 0 ? HelpBoxMessageType.Error : HelpBoxMessageType.Info);
            }
        }

        /// <summary>恢复稳定的文件和表名选择，内部重建不自动写持久设置。</summary>
        private void RestoreTableSelection()
        {
            ScannedTable selected = tables.FirstOrDefault(table => SameTable(table.SourcePath, table.Name, settings.SelectedSourcePath, settings.SelectedSheetName))
                ?? tables.FirstOrDefault();
            if (selected == null)
            {
                selectedSourceField.SetValueWithoutNotify(string.Empty);
                preview.Display(null, Array.Empty<ValidationIssue>(), null);
                return;
            }
            tableList.SetSelectionWithoutNotify(new[] { tables.IndexOf(selected) });
            SelectTable(selected, null, false);
        }

        /// <summary>显示当前表的读取或导出状态，并保留文件路径用于同名问题排查。</summary>
        private void BindTable(Label label, ScannedTable table)
        {
            string status;
            if (!exportStatuses.TryGetValue(TableKey(table.SourcePath, table.Name), out status))
                status = table.IsValid ? "校验通过" : "错误 " + table.Issues.Count;
            label.text = "[" + status + "]  " + Path.GetFileName(table.SourcePath) + "  /  " + table.Name
                + (table.Table == null ? string.Empty : "  ·  " + table.Table.Rows.Count + " 行");
            label.tooltip = table.SourcePath + "\n" + table.Name;
        }

        /// <summary>切换预览且保存用户选择；内部重建仅更新内存。</summary>
        private void SelectTable(ScannedTable table, ValidationIssue focusedIssue, bool persist)
        {
            settings.SelectedSourcePath = table.SourcePath;
            settings.SelectedSheetName = table.Name;
            selectedSourceField.SetValueWithoutNotify(table.SourcePath);
            preview.Display(table.Sheet, table.Issues, focusedIssue);
            if (persist) SaveSettings();
        }

        /// <summary>错误列表点击后切换到对应工作表并高亮其准确 Excel 坐标。</summary>
        private void LocateIssue(ValidationIssue issue)
        {
            ScannedTable table = tables.FirstOrDefault(item => SameTable(item.SourcePath, item.Name, issue.SourcePath, issue.SheetName));
            if (table == null) return;
            tableList.SetSelectionWithoutNotify(new[] { tables.IndexOf(table) });
            SelectTable(table, issue, true);
        }

        /// <summary>重新扫描后导出当前或全部表，统一显示写入和跳过摘要，只在生成物变化时刷新一次。</summary>
        private void ExportTables(bool allTables)
        {
            // 先保存原选择再刷新列表；源表被删除或改名时，不能悄悄导出第一张其他表。
            string requestedSource = settings.SelectedSourcePath;
            string requestedSheet = settings.SelectedSheetName;
            if (!allTables && (string.IsNullOrEmpty(requestedSource) || string.IsNullOrEmpty(requestedSheet)))
                throw new InvalidOperationException("请先扫描并选择需要导出的工作表。");
            ScanSources(false);
            // 设置保存失败必须发生在生成文件写入之前，避免已导出却漏掉后续资源刷新。
            SaveSettings();
            BatchExportResult result = BatchExportService.Export(scan, GetProjectRoot(), settings.JsonDirectory, settings.CodeDirectory,
                settings.GeneratedNamespace, allTables ? null : requestedSource, allTables ? null : requestedSheet);
            displayedIssues.Clear();
            displayedIssues.AddRange(tables.SelectMany(table => table.Issues));
            var summary = new StringBuilder("导出完成：成功 " + result.SuccessCount + " 张，失败 " + result.FailureCount
                + " 张；写入 " + result.WrittenCount + " 个文件，内容未变跳过 " + result.SkippedCount + " 个文件。");
            foreach (TableExportOutcome outcome in result.Tables)
            {
                exportStatuses[TableKey(outcome.SourcePath, outcome.Name)] = outcome.Success ? "导出成功" : "导出失败";
                summary.Append('\n').Append(outcome.Success ? "成功：" : "失败：").Append(outcome.Name).Append("\n源文件：").Append(outcome.SourcePath);
                foreach (ValidationIssue issue in outcome.Issues)
                {
                    summary.Append('\n').Append(issue);
                    if (!displayedIssues.Any(existing => existing.ToString() == issue.ToString() && existing.SourcePath == issue.SourcePath))
                        displayedIssues.Add(issue);
                }
                if (outcome.ExportResult != null)
                {
                    foreach (string file in outcome.ExportResult.WrittenFiles) summary.Append("\n写入：").Append(AbsoluteSourcePath(file));
                    foreach (string file in outcome.ExportResult.SkippedFiles) summary.Append("\n跳过：").Append(AbsoluteSourcePath(file));
                }
            }
            foreach (string warning in result.Warnings) summary.Append("\n提示：").Append(warning);
            tableList.Rebuild();
            issueList.Rebuild();
            issueLabel.text = "3. 错误定位（" + displayedIssues.Count + " 项，点击定位工作表和单元格）";
            ShowResult(summary.ToString(), result.FailureCount > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            Debug.Log("[Excel Data Tool] " + summary);
            if (result.WrittenCount > 0) AssetDatabase.Refresh();
        }

        /// <summary>在系统文件浏览器定位当前源文件，方便用户修复 Excel。</summary>
        private void RevealSelectedSource()
        {
            if (!File.Exists(settings.SelectedSourcePath)) throw new FileNotFoundException("当前源文件不存在，请检查来源列表。", settings.SelectedSourcePath);
            EditorUtility.RevealInFinder(settings.SelectedSourcePath);
        }

        /// <summary>比较稳定的文件及工作表标识，Windows 路径大小写不影响选择恢复。</summary>
        private static bool SameTable(string source, string sheet, string selectedSource, string selectedSheet)
        {
            StringComparison pathComparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(source, selectedSource, pathComparison) && string.Equals(sheet, selectedSheet, StringComparison.Ordinal);
        }

        /// <summary>合成用于展示导出状态的工作表键，换行符不会出现在合法文件或表名中。</summary>
        private static string TableKey(string source, string sheet)
        {
            return source + "\n" + sheet;
        }

        /// <summary>在交互边界捕获异常并显示可复制原因，窗口仍可继续调整来源或设置。</summary>
        private void RunAction(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                ShowResult("操作失败：" + exception.Message, HelpBoxMessageType.Error);
                Debug.LogError("[Excel Data Tool] " + exception.Message);
            }
        }

        /// <summary>同步摘要、完整详情和重编译后恢复的序列化消息。</summary>
        private void ShowResult(string message, HelpBoxMessageType messageType)
        {
            resultText = message ?? string.Empty;
            resultType = messageType;
            if (resultBox != null)
            {
                int lineBreak = resultText.IndexOf('\n');
                resultBox.text = lineBreak < 0 ? resultText : resultText.Substring(0, lineBreak);
                resultBox.messageType = messageType;
            }
            if (detailsField != null) detailsField.SetValueWithoutNotify(resultText);
        }
    }
}
