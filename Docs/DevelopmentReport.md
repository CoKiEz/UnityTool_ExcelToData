# 开发阶段报告

## 阶段一：核心导表闭环（2026-10-09）

**当前状态：用户已于 2026-10-10 确认第一阶段验收通过，并明确批准进入第二阶段。**

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

第一阶段的自动测试与开发时未运行项目记录保留如上；人工验收结论以用户于 2026-10-10 的通过确认为准。

## 阶段二：日常使用的编辑器工具（2026-10-10）

**当前状态：用户已于 2026-10-10 确认第二阶段验收通过，并授权第三阶段。**

### 实现与文件

- 支持添加多个 `.xlsx` 或递归源目录，稳定排序并去重，跳过 Excel `~$` 临时文件和符号链接/目录联接。一次扫描读取全部有效工作表，跨文件同名表（不区分大小写）双方都报错；单表导出也不能绕过当前扫描范围内的冲突。
- 批量按表隔离失败：损坏工作簿、非法类型/表名/ID、已有文件冲突会明确显示；其余合法表继续导出。结果逐表列出成功或失败，并统计实际写入与跳过的 JSON/C# 文件数。
- 完整 UI Toolkit 窗口提供源列表、工作簿/表列表、原始数据预览和可定位错误。预览显示三行表头及前 100 条非空数据，最多 30 列，保留真实 Excel 行号；限制之外的错误仍显示准确坐标和原值。
- 输入路径、输出路径、命名空间及当前选择保存到本工程 `UserSettings/ExcelDataTool.json`，关闭窗口或重启 Unity 后读取。界面构建本身不写设置；设置也有文件归属和路径保护。
- 增量写入先检查所有权，再比较内容；相同文本（兼容 CRLF/LF）保留原始字节及时间戳。只改数值时仅更新对应 JSON；内部归属记录无变化也不重写。命名空间默认值仍为 `ExcelDataTool.Generated`，自定义命名空间在生成前校验。
- 新增 `Card.xlsx`（`Card`、`Rarity`、`#说明`），与 `Enemy.xlsx` 共三个有效配置表，已使用正式导出管线生成相应示例。对已成功读取的工作簿中移除或更名的表，只提示过时产物并保留文件。

主要新增/修改文件：`Editor/Core/WorkbookScanner.cs`、`BatchExportService.cs`、`TableExporter.cs`、`TableExportResult.cs`；`Editor/UI/ExcelDataToolWindow.cs`、设置存储与预览辅助类；`Tests/Editor/StageTwoTests.cs`、`StageTwoEditorTests.cs`；`Assets/GameData/Excel/Card.xlsx` 与其生成物、归属记录。

未新增第三方依赖，继续使用 Unity 官方 Newtonsoft JSON 3.2.2 及第一阶段 ZIP/XML 读取器。没有制作运行时示例、迁移包或第三阶段 README。进入本阶段前用户已删除的 `~$Enemy.xlsx.meta` 和已有 Code Coverage 设置保持原样；用户验收后的 `Enemy!C4 = 120` 也保持原样。第一阶段回归仅在测试副本中恢复 100→120 的固定测试起点。

### 实际验证

| 状态 | 验证内容与证据 |
|---|---|
| PASS | Unity 自带 Mono/C# 编译全部核心；正式批量管线导出三个示例表，首次新增 6 个文件、跳过原有 Enemy 的 3 个文件；立即再次导出为 **0 写入、9 跳过**。 |
| PASS | 扫描/批量服务 20 项独立文件系统检查，包含递归去重、锁文件、损坏/占用/缺失、冲突双方阻止、合法表继续和当前选择失效；原始日志 `Temp/ExcelDataToolStageTwoScannerCheck/ScannerSmoke.log`。 |
| PASS | 导出器 20 项独立检查，包含时间戳不变、仅数值更新、CRLF 保持、锁定旁车后回滚和过时产物保留；自定义 `Game.System` 命名空间生成代码使用 `/warnaserror /doc` 编译通过。原始产物在 `Temp/ExcelDataToolExporterStageTwo/`。以上独立检查不冒充 Unity Test Runner。 |
| PASS | **Unity 6000.3.17f1 实际编译及 EditMode：69/69 通过，失败 0、跳过 0，退出码 0。** 包含第一阶段 42 项回归、第二阶段核心 20 项和设置/预览 7 项；实际覆盖重复表名双方阻止、非法表保留旧输出、锁文件回滚、时间戳不变、仅 JSON 更新、设置重载及 UI Toolkit 控件重建。[NUnit 原始 XML](TestResults/StageTwo-EditMode.xml)。 |
| FAIL → PASS | 首轮 67/69：旧共享字符串测试没有适配 Excel/WPS 重新保存后已有的 sharedStrings 部件；增量测试快照排除路径未统一 Windows 分隔符。已修复测试夹具，保留用户工作簿原样，并完整重跑至 69/69。无遗留失败用例。首轮 XML 与日志保留于 `.utmp/StageTwoValidation/`。 |
| PASS | 30 个功能文件与实际受测副本字节一致，全部具有 `.meta`；Enemy 源表、原有生成物及既有 Code Coverage 设置摘要未变；用户原先删除的 Excel 锁文件 `.meta` 未恢复。`git diff --check` 通过。[交付核对记录](TestResults/StageTwo-Verification.json)。 |
| NOT RUN | 用户可见窗口的实际点击、拖动尺寸、滚动预览和关闭/重开 Unity 的体验；这些仍需人工验收。 |

Unity 测试在工程内已有隔离副本 `.utmp/u` 执行，未关闭用户原工程。命令参数为 `-batchmode -nographics -projectPath <副本> -runTests -testPlatform EditMode -testResults <XML> -logFile <日志>`；完整日志位于 `.utmp/StageTwoValidation/Unity-StageTwo.log`。未新增依赖或更改既有工程配置，未 commit/push。

### 人工验收（五步）

1. 等待 Unity 编译完成，打开 **Tools → Excel Data Tool**，点击 **添加示例**、**扫描并校验**；列表应出现 `Enemy.xlsx / Enemy` 和 `Card.xlsx / Card、Rarity`，切换表核对中文、列类型及行号。
2. 点击 **批量导出**，应成功 3 表并具有 9 个 JSON/C# 产物；不修改数据再次导出，应显示 **写入 0、跳过 9**。选中 Rarity 后点击 **导出当前表**，应只统计该表。
3. 把 `Card!D4` 的 30.5 改成 31.5，保存后批量导出，应只写 1 个 JSON、跳过 8 个文件，C# 修改时间不变；随后还原数值并导出。
4. 将 `Card!A5` 暂改为 3001，保存后导出，检查 `A5` 错误定位及旧 Card JSON 保留，其他两表仍成功；恢复后，把 Enemy 另存为临时副本并添加为源，检查两个 Enemy 同名表均失败。验收后移除临时来源并恢复源表。非法表名、非法类型同样应定位并阻止该表导出。
5. 修改命名空间为 `MyGame.Config` 并保存设置，关闭再打开窗口，确认来源、路径和命名空间仍在；导出应只更新 6 个 C# 文件，Unity 编译成功。最后恢复默认命名空间并导出。

### 已知限制与阶段边界

- 扫描和导出由按钮触发，当前为同步处理；大型目录或工作簿会有等待时间，不监听 Excel 变化。批量统计中的失败数包含无法读取的工作簿占位项；写入/跳过数不含内部归属记录。
- 预览为受限原始单元格视图，不提供 Excel 内嵌编辑，也不显示超过上限的整张网格。错误超出预览范围时以准确地址和原始内容提示。
- 命名空间支持点分 ASCII C# 标识符，拒绝关键字及 `System` 根；自定义生成代码用完整框架类型名避免名称遮蔽。
- 既有表更换源位置或输出目录仍明确阻止静默覆盖，不自动迁移或删除旧产物；手改生成文件或丢失归属记录会触发保护。跨多表不是整体事务：合法表可以成功，失败表保留旧数据；单表可捕获的写入异常执行回滚，断电/终止进程限制延续第一阶段。
- 公式缓存、日期/复杂类型及文件规模上限延续第一阶段。跨工程移植与运行时读取示例属于第三阶段，尚未开始。

以上为第二阶段交付时的实现及验证记录；第三阶段新增内容见下文。

## 阶段三：通用化、自动化测试与最终交付（2026-10-10）

**当前状态：第三阶段开发及自动验证完成，等待用户验收。已停止开发。**

### 实现与架构

- 保持可复制的 Assets 插件结构，通用 Editor 和 Runtime 各有独立程序集；编辑器核心不引用任何 Enemy/Card、示例场景或运行时配置类型。示例生成代码使用 `GameData.Generated` 程序集，可选示例使用 `GameData.Examples`，测试程序集全部限定 Editor。
- 新增 `JsonTableLoader.Load<TTable>(TextAsset)` / `LoadJson<TTable>(string)`，从已加载资源读取固定 `rows` JSON；明确拒绝损坏结构、未知成员、类型转换失败，并禁用类型元数据激活。不读 Player 中的 Assets 磁盘路径，不管理存档或回写配置。
- 新增 `EnemyConfigExample` MonoBehaviour，引用实际 Enemy JSON 并读取 `EnemyTable` 和 `id == 1001` 的 `EnemyRow`。示例有独立场景及仅作用于示例生成程序集的 `link.xml`，便于验收及代码裁剪配置参考。
- 自动测试改用 `Tests/Fixtures` 固定工作簿，通过脚本编译位置定位，支持插件移动目录；不再依赖或修改用户的 `Assets/GameData/Excel`。补充 15 项编辑器边界测试、23 项通用运行时读取测试和 3 项真实示例 TextAsset 测试。
- 完成根 `README.md` 的安装、使用、表格协议、迁移与常见问题，以及 `Docs/ThirdPartyNotices.md` 的实际依赖版本和许可证记录。选择 Assets 目录迁移以减少包结构变更，无需引入 UPM 私有仓库或其他框架。

主要文件：`Assets/ExcelDataTool/Runtime/`、`Tests/Fixtures/`、`Tests/Editor/StageThreeTests.cs`、`Tests/Runtime/`；`Assets/GameData/Generated/Code/GameData.Generated.asmdef`、`Assets/GameData/Examples/`；`README.md`、`Docs/ThirdPartyNotices.md`。沿用官方 JSON 包 3.2.2（Newtonsoft 13.0.2）及现有 Test Framework 1.6.0；没有新增外部 DLL 或修改源表、既有生成 JSON/C#、业务场景和工程设置。

### 实际验证

| 状态 | 验证内容与证据 |
|---|---|
| PASS | 使用 Unity 6000.3.17f1 的 `-createProject` 在工程内 `.utmp/c3` 创建全新项目，退出码 0；未复制原工程的 Assets、ProjectSettings 或编译产物来冒充干净项目。 |
| PASS | 仅安装工具及其测试到 `Assets/Vendor/ExcelDataTool`，此时没有 GameData 或示例资产：实际编译成功，**107/107 EditMode 通过**（84 项 Editor、23 项 Runtime），失败/跳过 0，退出码 0。证明夹具及工具可以随安装目录迁移。[原始 XML](TestResults/StageThree-ToolOnly-EditMode.xml)。 |
| PASS | 干净项目随后添加两个真实源工作簿，通过正式服务从零导出三表 **9 个 JSON/C#**，再次导出为 **0 写入、9 跳过**。实际创建、显示并关闭 UI Toolkit 窗口，核心按钮存在且没有 IMGUI 主窗口；无界面进程不等同于可见窗口手工点击。 |
| PASS | 加入运行时示例后，干净项目和原工程隔离副本各自实际编译并完成 **110/110 EditMode**，失败/跳过 0，退出码均 0；包含从真实导入 TextAsset 反序列化真实 EnemyTable/EnemyRow 的 3 项示例测试。[干净工程 XML](TestResults/StageThree-CleanProject-EditMode.xml)、[原工程回归 XML](TestResults/StageThree-EditMode.xml)。 |
| PASS | **Windows x64 Mono Development Player 实际构建成功，0 错误、0 警告，并实际运行至退出码 0。** 程序在 `Application.isEditor == false` 下通过场景 TextAsset 读到 3 行 Enemy、`id=1001`、名称“哥布林”、`maxHealth=120`；与交付 JSON 一致。本工具 Editor 和测试程序集未进入构建产物。构建、运行和导出结果汇总见 [验证记录](TestResults/StageThree-Verification.json)。 |
| PASS | 45 个功能文件与实际受测原工程副本字节一致，通用工具与干净工程 Vendor 安装内容一致；53 个阶段开始时已有的源表/数据/场景/配置文件摘要不变，新增功能资产均具有 `.meta`。新场景正确引用交付脚本及 Enemy.json，未包含临时 Player 验证脚本。`git diff --check` 通过。 |
| FAIL → PASS | 干净项目首次解析 Test Framework 时出现 Package Manager 解包 `EPERM`，使用现有官方 **1.6.0** 包缓存后解决，未复制旧工程的脚本编译产物。新增外部工作表测试首轮误取关系文件中的首条 worksheet（实际指向应忽略的 `#说明`），已改为按 Enemy 的关系 ID 定位；完整重跑 107/107 及两套 110/110 通过，无遗留失败用例。 |
| NOT RUN | 用户可见窗口的实际操作和示例场景 Play Mode 观感；自动运行不能替代人工验收。 |
| NOT RUN | IL2CPP、移动端、WebGL、macOS/Linux 构建；本次目标为当前可用的 Windows Mono 环境。 |

Unity 测试命令使用 `-batchmode -nographics -projectPath <副本> -runTests -testPlatform EditMode -testResults <XML> -logFile <日志>`。干净导出与构建使用临时验证入口 `-executeMethod CleanProjectExport.Run` / `RuntimeSampleValidation.Run`，Player 使用 `ExcelDataToolSample.exe -batchmode -nographics -logFile <日志>`；原始日志、验证入口源码和首轮失败结果位于 `.utmp/StageThreeValidation/`，构建位于 `.utmp/c3/Build/`，均不进入 Git。新场景由 Unity 创建后复制回交付目录，原工程保持打开，既有场景未改动。未自动 commit/push。

### 人工验收（四步）

1. 在当前工程等待编译完成，打开 **Window → General → Test Runner → EditMode** 运行工具及示例测试；打开导表窗口，确认既有示例可继续扫描、预览和增量导出。
2. 打开 `Assets/GameData/Examples/Scenes/EnemyConfigExample.unity`，确认组件已引用 `Enemy.json`，进入 Play Mode，Console 应输出 `ID=1001`、哥布林及 JSON 中对应的属性；这是静态配置读取，不改源表或存档。
3. 按 README 的最小迁移步骤，在干净 Unity 6 工程安装官方 JSON 包并复制 Editor/Runtime；添加自己的 `.xlsx` 导出。需要自动测试时额外安装 Test Framework 并复制 Tests；不要求复制 GameData。
4. 按 README 的完整示例迁移步骤安装 GameData 及匹配的 ExportRecords，保留相对布局和 `.meta`，打开示例场景再运行。若改了命名空间或 Enemy 表结构，应同步修改可选示例代码。

### 已知限制

- 运行时示例固定使用默认命名空间和 Enemy 字段，是可选教学代码；生产项目只复制通用 Editor/Runtime 即可使用自己的表，不需要 Enemy 类型。
- 反射反序列化涉及代码裁剪，示例已提供 `link.xml`；更换生成程序集、脚本后端或目标平台后仍需按实际 Player 构建验证，不能据此声称 IL2CPP 已验证。
- 第三阶段不改变既有输出迁移保护、同步扫描、预览规模、公式缓存和 XLSX 类型/文件规模限制。工程外源文件的归属记录包含路径摘要，换机器时需特别核对；不自动接管、迁移或删除未知文件。
