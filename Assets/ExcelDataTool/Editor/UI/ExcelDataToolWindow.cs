using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExcelDataTool.Editor.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelDataTool.Editor.UI
{
    /// <summary>
    /// 提供选择 Excel 工作簿、校验当前工作表以及导出 JSON 和 C# 的编辑器窗口。
    /// </summary>
    /// <remarks>
    /// 仅负责 UI Toolkit 交互；读取、校验和安全写入分别委托给核心服务。
    /// 操作异常会显示在窗口和 Console 中，不会直接传播到按钮事件。
    /// </remarks>
    public sealed class ExcelDataToolWindow : EditorWindow
    {
        private const string SamplePath = "Assets/GameData/Excel/Enemy.xlsx";

        // 只保存本窗口的序列化状态，以便脚本重编译后重建；不引入跨会话设置系统。
        [SerializeField] private string sourcePath = string.Empty;
        [SerializeField] private string jsonDirectory = "Assets/GameData/Generated/Json";
        [SerializeField] private string codeDirectory = "Assets/GameData/Generated/Code";
        [SerializeField] private string selectedSheetName = string.Empty;
        [SerializeField] private string resultText = "请选择 .xlsx 文件，或点击“使用 Enemy 示例”。";
        [SerializeField] private HelpBoxMessageType resultType = HelpBoxMessageType.Info;

        private WorkbookData workbook;
        private TextField sourceField;
        private DropdownField sheetField;
        private Label informationLabel;
        private HelpBox resultBox;
        private TextField detailsField;

        /// <summary>
        /// 创建窗口的 UI Toolkit 可视树，并在 Domain Reload 后恢复可操作的界面。
        /// </summary>
        /// <remarks>
        /// 此 Unity 生命周期回调不接收参数、不返回结果，也不读取或导出工作簿。
        /// 工作簿缓存不参与序列化；下次读取、校验或导出时会重新加载源文件。
        /// </remarks>
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            workbook = null;
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.paddingLeft = 12;
            scroll.style.paddingRight = 12;
            scroll.style.paddingTop = 12;
            scroll.style.paddingBottom = 12;
            rootVisualElement.Add(scroll);

            var title = new Label("Excel → JSON + C#");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            scroll.Add(title);
            scroll.Add(new HelpBox("三行表头依次为：字段名、类型、说明。第 4 行起为数据；每张表必须包含 int 类型的 id。", HelpBoxMessageType.Info));

            sourceField = new TextField("Excel 文件") { value = sourcePath };
            sourceField.tooltip = "接受 .xlsx 文件的绝对路径或相对于工程根目录的路径。";
            sourceField.RegisterValueChangedCallback(evt =>
            {
                sourcePath = evt.newValue;
                workbook = null;
                selectedSheetName = string.Empty;
                sheetField.choices = new List<string>();
                sheetField.SetValueWithoutNotify(string.Empty);
                sheetField.SetEnabled(false);
                informationLabel.text = "输入路径已改变，请重新读取工作簿。";
                ShowResult("请读取并校验新的源文件。", HelpBoxMessageType.Info);
            });
            scroll.Add(sourceField);

            var sourceButtons = CreateButtonRow();
            sourceButtons.Add(new Button(() => RunAction(BrowseSource)) { text = "选择 .xlsx…" });
            sourceButtons.Add(new Button(() => RunAction(UseSample)) { text = "使用 Enemy 示例" });
            sourceButtons.Add(new Button(() => RunAction(() => ReadAndValidate())) { text = "读取工作簿" });
            scroll.Add(sourceButtons);

            sheetField = new DropdownField("当前工作表", new List<string>(), -1);
            sheetField.SetEnabled(false);
            sheetField.RegisterValueChangedCallback(evt =>
            {
                selectedSheetName = evt.newValue;
                RunAction(ValidateCachedSelection);
            });
            scroll.Add(sheetField);

            informationLabel = new Label("尚未读取工作簿。脚本重编译后可点击“读取工作簿”重新载入。");
            informationLabel.style.whiteSpace = WhiteSpace.Normal;
            informationLabel.style.marginTop = 4;
            informationLabel.style.marginBottom = 12;
            scroll.Add(informationLabel);

            var jsonField = new TextField("JSON 输出目录") { value = jsonDirectory };
            jsonField.RegisterValueChangedCallback(evt => jsonDirectory = evt.newValue);
            jsonField.tooltip = "指定工程 Assets 内的目录。导出器会检查目录边界及现有文件归属。";
            scroll.Add(jsonField);
            var codeField = new TextField("C# 输出目录") { value = codeDirectory };
            codeField.RegisterValueChangedCallback(evt => codeDirectory = evt.newValue);
            codeField.tooltip = "指定工程 Assets 内的目录；已有手写脚本不会被覆盖。";
            scroll.Add(codeField);
            scroll.Add(new Label("生成命名空间：ExcelDataTool.Generated"));

            var exportButtons = CreateButtonRow();
            exportButtons.Add(new Button(() => RunAction(() => ReadAndValidate())) { text = "校验当前表" });
            exportButtons.Add(new Button(() => RunAction(ExportCurrentSheet)) { text = "导出当前表" });
            scroll.Add(exportButtons);
            resultBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            scroll.Add(resultBox);
            detailsField = new TextField("操作详情") { multiline = true, isReadOnly = true };
            detailsField.style.minHeight = 120;
            detailsField.tooltip = "可选择并复制错误位置或生成路径。";
            scroll.Add(detailsField);
            ShowResult(resultText, resultType);
        }

        /// <summary>
        /// 从 Unity 菜单打开可停靠的导表窗口。
        /// </summary>
        [MenuItem("Tools/Excel Data Tool")]
        private static void OpenWindow()
        {
            var window = GetWindow<ExcelDataToolWindow>();
            window.titleContent = new GUIContent("Excel Data Tool");
            window.minSize = new Vector2(460, 440);
            window.Show();
        }

        /// <summary>
        /// 创建允许窄窗口换行的操作按钮容器。
        /// </summary>
        /// <returns>带水平布局和基础间距的可视容器。</returns>
        private static VisualElement CreateButtonRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 6;
            row.style.marginBottom = 6;
            return row;
        }

        /// <summary>
        /// 获取当前 Unity 工程根目录，避免依赖进程工作目录。
        /// </summary>
        /// <returns>工程根目录的绝对路径。</returns>
        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        /// <summary>
        /// 打开系统文件选择器，并在用户选择文件后立即读取。
        /// </summary>
        private void BrowseSource()
        {
            string path = EditorUtility.OpenFilePanel("选择 Excel 工作簿", GetProjectRoot(), "xlsx");
            if (string.IsNullOrEmpty(path))
                return;

            sourceField.value = path;
            ReadAndValidate();
        }

        /// <summary>
        /// 载入随工具提供的 Enemy 工作簿，缩短首次验收操作。
        /// </summary>
        private void UseSample()
        {
            sourceField.value = SamplePath;
            ReadAndValidate();
        }

        /// <summary>
        /// 重新读取磁盘工作簿、恢复当前工作表选择并校验。
        /// </summary>
        /// <param name="requireSelectedSheet">导出时要求先前选定的工作表仍然存在，防止静默导出其他表。</param>
        /// <returns>当前工作表的校验结果，失败时不包含可导出的数据。</returns>
        /// <exception cref="ArgumentException">未指定源文件路径时抛出。</exception>
        /// <exception cref="IOException">源文件无法读取或不含有效工作表时抛出。</exception>
        private ValidationResult ReadAndValidate(bool requireSelectedSheet = false)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("请先选择一个 .xlsx 文件。");

            string resolvedPath = Path.GetFullPath(Path.IsPathRooted(sourcePath)
                ? sourcePath
                : Path.Combine(GetProjectRoot(), sourcePath));
            workbook = XlsxWorkbookReader.Read(resolvedPath);
            var names = workbook.Sheets.Where(sheet => !sheet.Name.StartsWith("#", StringComparison.Ordinal))
                .Select(sheet => sheet.Name).ToList();
            if (names.Count == 0)
                throw new InvalidDataException(Path.GetFileName(resolvedPath) + "：工作簿没有有效数据表（以 # 开头的工作表会被忽略）。");

            if (requireSelectedSheet && !string.IsNullOrEmpty(selectedSheetName) && !names.Contains(selectedSheetName))
                throw new InvalidDataException(Path.GetFileName(resolvedPath) + "：原先选定的工作表“" + selectedSheetName
                    + "”已不存在，请重新读取工作簿并选择工作表。");

            if (!names.Contains(selectedSheetName))
                selectedSheetName = names[0];

            sheetField.choices = names;
            sheetField.SetValueWithoutNotify(selectedSheetName);
            sheetField.SetEnabled(true);
            return ValidateSelection();
        }

        /// <summary>
        /// 切换工作表时校验当前已读取的内容；显式校验和导出仍会重新读取磁盘。
        /// </summary>
        private void ValidateCachedSelection()
        {
            if (workbook != null)
                ValidateSelection();
        }

        /// <summary>
        /// 校验所选工作表并展示字段数、数据量及带单元格坐标的错误。
        /// </summary>
        /// <returns>核心校验器产生的结果。</returns>
        private ValidationResult ValidateSelection()
        {
            RawSheet sheet = workbook.Sheets.First(item => item.Name == selectedSheetName);
            ValidationResult result = TableValidator.Validate(sheet);
            string summary = "工作簿：" + Path.GetFileName(workbook.SourcePath)
                + "  |  有效工作表：" + sheetField.choices.Count
                + "  |  当前表：" + sheet.Name;
            if (result.IsValid)
            {
                informationLabel.text = summary + "\n字段：" + result.Table.Columns.Count
                    + "  |  数据行：" + result.Table.Rows.Count;
                ShowResult("校验通过，可以导出当前表。", HelpBoxMessageType.Info);
            }
            else
            {
                informationLabel.text = summary;
                ShowResult("校验失败，共 " + result.Issues.Count + " 个错误；未写入文件。\n"
                    + string.Join("\n", result.Issues.Select(issue => issue.ToString())), HelpBoxMessageType.Error);
            }
            return result;
        }

        /// <summary>
        /// 从磁盘重新读取和校验当前表，成功写出全部文件后刷新 Unity 资源。
        /// </summary>
        private void ExportCurrentSheet()
        {
            // 不能导出旧缓存：用户可能在保持工具窗口打开时修改 Excel。
            ValidationResult result = ReadAndValidate(true);
            if (!result.IsValid)
                return;

            IReadOnlyList<string> paths = TableExporter.Export(result.Table, GetProjectRoot(), jsonDirectory, codeDirectory);
            string message = "导出成功：" + result.Table.Name + "，" + result.Table.Rows.Count
                + " 行，" + paths.Count + " 个文件。\n" + string.Join("\n", paths);
            // 先保存结果再触发资源导入，脚本重新编译后窗口仍能显示本次输出路径。
            ShowResult(message, HelpBoxMessageType.Info);
            Debug.Log("[Excel Data Tool] " + message);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 在 UI 边界处理操作异常，避免文件问题导致未捕获的按钮回调异常。
        /// </summary>
        /// <param name="action">需要执行的读取、校验或导出操作。</param>
        private void RunAction(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                ShowResult("操作失败：" + exception.Message, HelpBoxMessageType.Error);
                Debug.LogError("[Excel Data Tool] " + exception.Message);
            }
        }

        /// <summary>
        /// 同步窗口提示、可复制详情和用于重编译恢复的结果文本。
        /// </summary>
        /// <param name="message">包含成功路径或校验位置的完整消息。</param>
        /// <param name="messageType">消息的严重级别。</param>
        private void ShowResult(string message, HelpBoxMessageType messageType)
        {
            resultText = message;
            resultType = messageType;
            if (resultBox != null)
            {
                int lineBreak = message.IndexOf('\n');
                resultBox.text = lineBreak < 0 ? message : message.Substring(0, lineBreak);
                resultBox.messageType = messageType;
            }
            if (detailsField != null)
                detailsField.SetValueWithoutNotify(message);
        }
    }
}
