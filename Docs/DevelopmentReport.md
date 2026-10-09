# 开发阶段报告

## 阶段一：核心导表闭环（2026-10-09）

**当前状态：等待用户验收。仅实施第一阶段。**

### 实现与主要文件

- 新增 `Tools/Excel Data Tool` 菜单及纯 UI Toolkit 窗口，可选择文件/示例、选择当前工作表、查看字段和行数、校验与导出。每次显式校验和导出都会重新读取磁盘，避免 Excel 修改后误用缓存。
- 实现只读 XLSX 解析、三行表头校验及 `int / float / double / bool / string` 转换。错误提供源文件、表名、Excel 坐标和原因；校验失败没有可导出对象，不写入旧结果。
- 按固定协议生成 UTF-8 `{"rows":[...]}` JSON、`ExcelDataTool.Generated` 命名空间的行类和表类。生成代码具有自动生成标记、中文 XML 文档注释，不依赖 UnityEditor。
- 实现工程 Assets 路径边界、非生成文件及手改文件保护、同名来源冲突检查；先校验所有目标再安全替换，普通文件写入失败时回滚。
- 交付真实 `Assets/GameData/Excel/Enemy.xlsx`，包含三个敌人和 `#说明` 页；`Assets/GameData/Generated/Json/Enemy.json` 与 `Code/EnemyRow.cs`、`EnemyTable.cs` 均由实际导出生成。

主要代码位于 `Assets/ExcelDataTool/Editor/Core/`（读取、模型、校验、导出）及 `Editor/UI/ExcelDataToolWindow.cs`。基础测试位于 `Assets/ExcelDataTool/Tests/Editor/StageOneTests.cs`，程序集限定为 Editor。新增资源均带 `.meta`；既有场景、输入资产、ProjectSettings 和 README 未修改，未 commit/push。

### 技术取舍与依赖

- 工程实际版本为 **Unity 6000.3.17f1**；开始时 Git 工作区干净，无已有实现和阶段报告。
- JSON 使用 Unity 官方 `com.unity.nuget.newtonsoft-json 3.2.2`，通过 manifest 与 lock 固定版本，仅增加此依赖。其 Newtonsoft.Json 版本为 13.0.2，第三方部分 MIT，Unity 包装部分 Unity Companion License；许可证随官方包提供。[Unity 包文档](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html)。下载包 SHA-256：`5db19a6ec4478ab974922d155aa32b952bc34abadaa78afc7973cf049b62db41`。
- XLSX 采用本项目维护的受限只读 OOXML 实现，使用 Unity 自带 .NET ZIP/XML API，无 Excel DLL 或 Office 安装要求。直接保留公式缓存状态，避免将缺失的缓存误作合法空字符串；实现依照 [Microsoft Cell 文档](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.cell)。支持共享/内联富文本、稀疏地址、原生布尔和 Strict/Transitional 命名空间，禁用 DTD/外部实体。
- UI 使用 `CreateGUI()` 中构建 VisualElement 的等效 UI Toolkit 方式，避免最小窗口引入额外布局资源加载路径。没有 IMGUI 主窗口。
- 标准 JSON 不能嵌入代码标记，因此用 `Assets/ExcelDataTool/ExportRecords/<表名>.export.json` 保存归属、源标识、路径和摘要；此记录需与生成物一起保留。摘要允许 Git 的 CRLF/LF 转换，但拒绝其他内容改动；并发检测仍比较原始字节。
- 示例由本机已有 openpyxl 生成，它仅是开发时制作示例的工具，Unity 用户不需要 Python 或 openpyxl。

### 实际验证

| 状态 | 验证项目与证据 |
|---|---|
| PASS | 实际 `Enemy.xlsx` → 读取 → 校验 → 导出三个文件 → JSON 回读，验证三行中文、生命值和 Boss 布尔值。随后将生成代码与核心一起用 .NET 编译，0 警告、0 错误。 |
| PASS | 读取器独立编译与 15 项断言：共享/内联富文本、稀疏列、布尔、转义、公式缓存与缺缓存、日期拒绝、大小写重复表名等。原始日志：`Temp/ExcelDataToolValidation/ReaderCheck/ReaderCheck.log`。此项不等同于 Unity Test Runner。 |
| PASS | Unity 自带 Mono/C# 编译导出器；15 项文件系统检查通过，包括非工具文件保护、手改保护、CRLF、路径限制、真实锁定第三个输出文件时恢复前面已写文件及归属记录。脚本：`Temp/ExcelDataToolExporterCheck/ExporterSmoke.cs`。 |
| PASS | **Unity 6000.3.17f1 实际编译及 EditMode：42/42 通过，失败 0、跳过 0，进程退出码 0。** 原工程保持打开，在工程内 `.utmp/u` 使用相同 Assets、Packages、ProjectSettings 的副本运行；包含 UI Toolkit 重复 CreateGUI、真实 XLSX 修改 100→120、重复 ID 保留旧输出。证据：[NUnit 原始 XML](TestResults/StageOne-EditMode.xml)。这不是对当前可见窗口的手工点击验收。 |
| FAIL → PASS | 首轮 Unity 编译发现测试 JSON DLL 引用、NUnit 不支持的特性及 CompressionLevel 同名问题，均已修复并通过上述完整重跑。较长临时路径还曾导致 Package Manager 导入失败；改用短路径和既有可信包缓存后完成全部包解析。无遗留失败测试。 |
| NOT RUN | 用户当前可见 Unity 窗口中的真实鼠标操作、布局观感和 Excel 软件编辑后的手工导出；自动检查不能替代这部分验收。 |

批处理实际使用 `Unity.exe -batchmode -nographics -projectPath <工程内验证副本> -runTests -testPlatform EditMode -testResults <结果XML> -logFile <日志>`，未使用 `-quit` 提前中断测试。完整 Unity 日志在 `Temp/ExcelDataToolValidation/Unity-EditMode.log`（本地忽略目录）；持久化 XML 与最终文件核对摘要位于 `Docs/TestResults/`。开发测试没有修改交付示例中的 100/1002，人工验收可以直接从初始数据开始。

### 最少人工验收（四步）

1. 回到原工程 Unity，等待包解析和编译完成，确认 Console 无新增编译错误；打开 **Tools → Excel Data Tool**，点击 **使用 Enemy 示例**，应显示 Enemy、6 个字段、3 行。
2. 点击 **导出当前表**，检查 `Assets/GameData/Generated/Json/Enemy.json` 的三条数据，以及 `Code/EnemyRow.cs`、`EnemyTable.cs`；等待脚本编译完成。关闭再打开窗口仍应可操作。
3. 在 Excel/WPS/LibreOffice 中把 `Enemy!C4` 的 `100` 改成 `120` 并保存，直接再次导出；JSON 第一条的 `maxHealth` 应变成 120。
4. 把 `Enemy!A5` 改成 `1001` 并保存，点击导出；应显示 `Enemy.xlsx / Enemy / A5` 重复 ID 错误，旧 JSON 仍保留第 3 步的有效内容。最后将 A5 恢复 1002、C4 恢复 100，保存并导出。

可选复查：**Window → General → Test Runner → EditMode**，运行 `ExcelDataTool.Editor.Tests`。

### 已知限制

- 第一阶段只导出当前工作簿中的当前表。批量扫描、数据网格预览、跨会话设置、内容不变不重写、运行时示例和迁移交付保留给后续阶段。
- 类型名必须精确小写；布尔接受原生 Excel 布尔及不区分大小写的 true/false 文本，不接受普通数字/文本 0/1。int 只接受范围内精确整数，Excel 保存成 1001.0 或科学记数法时可还原；真正的小数、溢出和非有限浮点数被拒绝。空字符串保留，空非字符串报错。
- 公式只使用文件已保存的缓存，无法检查缓存是否过时；缺缓存会报错。日期、合并配置单元格、加密工作簿及协议未规定的复杂类型不受支持。
- 只读解析上限：单个 ZIP 部件 32 MiB、解压总计 128 MiB、10000 个部件、500000 个实际单元格。超过上限明确拒绝。
- 已导出的表更换源位置或输出目录会阻止静默覆盖；阶段一没有自动迁移/清理功能。手改生成物或丢失归属记录也会阻止覆盖，应先备份并检查冲突，不能直接删除未知文件。
- 普通可捕获的写入错误会回滚；断电或进程被终止不能保证多个文件同时提交，必要时需检查导出目录保留的 `.tmp.backup`。不自动删除过时生成物。

完成以上验收后，请明确回复 **「阶段一通过，进入第二阶段」** 再开始下一阶段。
