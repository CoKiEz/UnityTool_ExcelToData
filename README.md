# UnityTool_ExcelToData

通用的 Unity 编辑器扩展工具：Excel 转 JSON 和 C#。

使用 UI Toolkit 管理多个 `.xlsx` 文件或源目录，读取三行表头，校验并预览数据，生成可供游戏运行时使用的强类型配置。每个工作表生成一个行类型、一个表容器和一个 JSON；增加数据行不需要增加脚本。

目标环境为 **Unity 6.3 LTS / 6000.3.17f1**，Windows 优先。实际编译、自动测试、迁移验证及尚未运行的项目统一记录在 [开发报告](Docs/DevelopmentReport.md)，本说明不代替测试结果。

## 在当前工程使用

1. 等待 Unity 完成依赖解析和编译，打开 **Tools → Excel Data Tool**。
2. 点击 **添加示例**，或通过 **添加文件… / 添加目录… / 添加路径** 配置自己的来源。目录会递归扫描；可重复添加文件，也可移除或清空来源，操作不会删除磁盘文件。
3. 点击 **扫描并校验**。示例包含 `Enemy.xlsx / Enemy` 和 `Card.xlsx / Card、Rarity` 三张有效表。点击表查看原始值；点击错误条目定位文件、工作表和单元格。
4. 在“输出设置”中确认路径和命名空间，点击 **导出当前表** 或 **批量导出**。每次导出都会重新扫描全部已配置来源，检查冲突后再写文件。
5. 检查成功/失败表数及写入/跳过文件数。未改内容再次导出，应全部跳过；仅修改某个数值时，只有对应 JSON 变化。生成 C# 变化后等待 Unity 编译完成。

表格预览保留实际 Excel 行号，显示三行表头及前 **100 条非空数据、最多 30 列**。错误超过显示范围时仍显示坐标和受限长度的原值。完整结果和路径可以复制，“定位源文件”可打开文件所在位置。

来源、输出目录、命名空间和当前工作表选择保存在工程内 `UserSettings/ExcelDataTool.json`；文本设置在回车或结束编辑后保存，也可显式点击“保存设置”。该文件属于本地用户设置，不需要提交到版本控制。

## 安装到其他 Unity 项目

本工具采用可复制的 `Assets` 目录结构，无需重建目标工程，也无需复制本工程的场景、输入系统或项目设置。

1. 在目标 Unity 6 项目通过 Package Manager 按包名安装 **`com.unity.nuget.newtonsoft-json`，版本 `3.2.2`**。已有该包时沿用目标项目兼容的依赖配置，不另外导入同名 DLL。操作入口见 [Unity 按名称安装包](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-ui-quick.html)。
2. 复制 `Assets/ExcelDataTool/Editor/`，保留其中的 `.meta` 和 `ExcelDataTool.Editor.asmdef`。推荐保留 `Assets/ExcelDataTool/` 这个父目录；不要覆盖目标工程已有的同名未知文件。
3. 如需下面的运行时 JSON 辅助类，再复制 `Assets/ExcelDataTool/Runtime/` 及 `.meta`。只需要导表的项目不必安装此目录。
4. 等待编译，打开工具，添加目标项目自己的 `.xlsx` 或源目录，校验后导出。最小安装没有 GameData 示例，此时直接添加自己的源表即可。

Newtonsoft 官方包 `3.2.2` 对应 Newtonsoft.Json `13.0.2`；依赖说明见 [Unity 包文档](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html)。不要用本工程的整个 `Packages/manifest.json` 或 `ProjectSettings/` 替换目标项目的配置。

| 目录 | 用途 | 迁移选择 |
|---|---|---|
| `Assets/ExcelDataTool/Editor/` | UI、读取、校验、扫描和导出；Editor 专用程序集 | 必需 |
| `Assets/ExcelDataTool/Runtime/` | `TextAsset` → 强类型对象的通用辅助类，无编辑器依赖 | 可选 |
| `Assets/ExcelDataTool/Tests/` | 核心及运行时自动测试、固定工作簿 Fixtures | 可选，按下方测试说明选择 |
| `Assets/ExcelDataTool/ExportRecords/` | 当前数据项目的输出归属及摘要记录 | 新独立数据项目不复制；迁移已有输出时需要 |
| `Assets/GameData/` | 源表、生成物、运行时示例和示例场景 | 可选完整示例 |
| `UserSettings/ExcelDataTool.json` | 当前用户的路径、命名空间和窗口选择 | 在目标项目重新配置 |

复制目录时同时保留目录旁的 `.meta`。工具没有将 Excel 解析器放入 Runtime；生成的普通 C# 类型本身不依赖 UnityEditor 或本工具运行时代码。

Editor/Runtime 可以放到其他插件目录；归属记录仍固定写入 `Assets/ExcelDataTool/ExportRecords/`，不会随插件目录移动。

## 迁移源表和已有生成物

**新独立数据项目：**只安装所需工具目录，再复制需要的源表并重新导出。不要带入本工程的 `ExportRecords` 或 `UserSettings`，也不要只复制旧生成文件而遗漏归属记录后期待自动覆盖。

**延续已有数据项目：**同时复制源表、对应 JSON/C# 生成物及 `Assets/ExcelDataTool/ExportRecords/<表名>.export.json`，保留三者在工程内的相对路径、文件内容和 `.meta`。记录中的工程内源身份使用相对路径，因此在保持相对布局的条件下可以迁移整个工程目录；输出目录保持不变，首次迁移先沿用原命名空间，再执行扫描和导出。之后可通过工具正常修改命名空间，并同步修改使用生成类型的业务代码。

记录还校验输出内容摘要，不支持通过修改记录来强行接管文件。工程外来源绑定其路径身份，更换外部源位置可能触发同名来源保护；跨操作系统的路径大小写差异也需要单独验证。更名、移动源表或切换已有输出目录时，请先备份并检查冲突，不要直接删除未知文件来绕过保护。

如需完整运行时演示，可安装 `Editor/`、`Runtime/`，再复制 `Assets/GameData/` 和对应三个表的 `ExportRecords`。其中 `GameData/Examples/Tests/` 是可选的 Editor 测试目录：没有安装 Test Framework 时不要复制此目录；需要示例测试时先安装下文的测试依赖。示例场景、脚本和 JSON 的引用通过 `.meta` 保持。

## Excel 输入协议

仅接受 `.xlsx`，不需要安装 Excel 或 Python；Excel、WPS、LibreOffice 等工具保存的有效工作簿可作为输入。一个普通工作表对应一张配置表，`#说明` 等以 `#` 开头的工作表不参与导出，`~$` 临时锁文件会被跳过。

| Excel 行 | 内容 | 例子 |
|---|---|---|
| 1 | C# 字段名 | `id`、`name`、`maxHealth` |
| 2 | 精确小写的字段类型 | `int`、`string`、`int` |
| 3 | 中文说明，可空 | 唯一 ID、名称、最大生命值 |
| 4 起 | 数据 | `1001`、哥布林、`120` |

- 表名遵循 `^[A-Z][A-Za-z0-9_]*$`；字段名遵循 `^[a-z][A-Za-z0-9_]*$`，不得为空、重复或使用 C# 保留字。系统保留文件名也会被拒绝。
- 表名在当前扫描范围以及已有导出记录中必须唯一，大小写差异不能用于区分两个表。请把本项目需要共同校验的所有来源加入列表。
- 每表必须包含 **`id`，类型为 `int`**；每条记录的 ID 必须有效且不重复。
- 字符串保留普通文本和中文，空字符串输出 `""`。整行全空跳过；其他类型为空报错。字段名、类型不能为空，说明允许为空。

| 类型 | 接受的值 | 拒绝的值 |
|---|---|---|
| `int` | 32 位有符号范围内的精确整数；Excel 保存成 `1001.0` 或等值科学记数法时可还原 | 真正的小数、溢出、空值 |
| `float` / `double` | 使用 `.` 小数点、与电脑区域设置无关的有限数值 | NaN、Infinity、溢出、空值 |
| `bool` | Excel 原生布尔格，或不区分大小写的 `true` / `false` 文本 | 普通数值或文本 `0` / `1`、空值 |
| `string` | 普通文本、中文及空字符串 | 不支持的日期类型、读取错误 |

公式只读取工作簿保存的缓存值，不负责计算；缓存缺失或不能转换为目标字段类型时会定位报错，包括字符串字段中的无缓存公式。缓存是否过时无法由工具判断，请在表格软件中重新计算并保存。

完整契约见 [开发需求文档](Docs/ExcelDataTool_DevelopmentSpec.md)。不支持数组、字典、嵌套对象、自动枚举、日期或 Unity 对象字段。

## 输出、增量和保护

默认生成位置：

```text
Assets/GameData/Generated/Json/Enemy.json
Assets/GameData/Generated/Code/EnemyRow.cs
Assets/GameData/Generated/Code/EnemyTable.cs
Assets/ExcelDataTool/ExportRecords/Enemy.export.json
```

JSON 是 UTF-8 标准对象，根字段固定为 `rows` 数组。JSON 键与 C# 字段名一致；每行的数据放在 JSON 中，不嵌入脚本常量。默认命名空间为 `ExcelDataTool.Generated`，可在输出设置中改为合法的点分 C# 命名空间。

输出目录必须处于当前工程 `Assets` 内，不能位于 `Editor` 目录，也不接受 `.` / `..` 路径段或符号链接绕过。表有校验错误时不写新结果，旧文件保持原样；批量中其他合法表仍可成功。已有手写文件、没有归属记录的文件、被手动修改的生成物均会阻止覆盖。

文件按内容比较后增量更新，兼容 CRLF/LF 换行；未变化的文件保留原始字节和时间戳。JSON/C# 的写入和跳过计入窗口摘要，内部归属记录不计入这两个文件数。发现过时产物时仅提示，不自动删除。普通可捕获的单表写入错误会回滚；多表之间不是整体事务，断电或强制终止时需检查输出及可能保留的备份。

## 在运行时读取配置

安装可选的 Runtime 目录后，可通过 `ExcelDataTool.Runtime.JsonTableLoader.Load<TTable>(TextAsset)` 把 JSON 资源转换为生成的表类型；已有字符串可使用 `LoadJson<TTable>(string)`。加载输入是已取得的 `TextAsset`，可由 Inspector 引用或项目自己的资源系统提供；Player 中不通过 `Assets/...` 磁盘路径读取配置。

读取器会拒绝损坏的根结构、非数组 `rows`、未知 JSON 成员及不能转换的值；表结构变化时应同时更新生成的 C# 和 JSON。它负责读取合法导表结果，不替代编辑器的数据校验，也不会为缺失字段重新推导配置值。

完整示例位于 `Assets/GameData/Examples/`：

1. 打开 `Assets/GameData/Examples/Scenes/EnemyConfigExample.unity`。
2. 检查场景中的 `EnemyConfigExample` 组件引用了 `Assets/GameData/Generated/Json/Enemy.json`。
3. 进入 Play Mode，示例读取 `EnemyTable`，查找 `id == 1001` 的记录，并输出名称和属性。当前交付 Enemy 样例保留用户验收后的 `maxHealth = 120`。

Runtime、示例生成代码和示例行为分别使用 `ExcelDataTool.Runtime`、`GameData.Generated`、`GameData.Examples` 程序集。`GameData.Examples.EnemyConfigExample` 是可选的具体业务示例，使用默认的 `ExcelDataTool.Generated` 命名空间及 Enemy 字段；更改示例表名、字段或生成命名空间后，需要同步更新这个示例脚本及其程序集引用。只复制通用 Editor/Runtime 的项目不依赖 Enemy、Card、示例场景或生成类型。

示例的 `Assets/GameData/Examples/link.xml` 为 `GameData.Generated` 保留通过反射读取的类型和字段。自己的项目使用了代码裁剪时，应为自己的生成程序集设置对应保留规则；迁移并更改程序集名称后同步检查该文件。具体脚本后端与平台验证状态以开发报告为准，不能由提供了 `link.xml` 推断 IL2CPP 已验证。

程序集配置和反射类型保留可参照 Unity 官方的 [Assembly Definition 格式](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/script-compilation/assembly-definition-files/file-format) 与 [代码裁剪说明](https://docs.unity.cn/Manual/ManagedCodeStripping.html)。

运行时对象用于读取静态配置；战斗 HP、金币和存档应另行管理，不回写源表或原始配置 JSON。目标平台、脚本后端和代码裁剪设置应在自己的 Player 构建中验证，不能把编辑器内测试直接视作所有平台发布验证。

## 自动测试与验收证据

核心测试位于 `Assets/ExcelDataTool/Tests/Editor/`，固定工作簿位于同级 `Tests/Fixtures/`。只复制这两个子目录即可在仅安装 Editor、没有 `Assets/GameData/` 或 Examples 的项目中运行核心测试，不依赖或修改用户示例。保留 Editor 与 Fixtures 的相邻目录关系。

可选的运行时辅助类测试位于 `Tests/Runtime/`，需要同时安装 `Assets/ExcelDataTool/Runtime/`；它们在 Editor 中执行，不依赖 GameData。完整复制 Tests 时应一并安装 Runtime。

测试需 Unity Test Framework，本工程使用 `com.unity.test-framework 1.6.0`；通过 **Window → General → Test Runner → EditMode** 运行 `ExcelDataTool.Editor.Tests`。如额外安装运行时辅助类测试，也在对应程序集下运行。测试框架说明见 [Unity Test Framework 文档](https://docs.unity3d.com/Packages/com.unity.test-framework@1.6/manual/index.html)。临时工作簿和输出写入工程内独立测试目录，测试不要求外部 Excel 软件。

执行过的命令、真实通过/失败结果、原始 NUnit XML、干净项目迁移过程及人工验收步骤见 [DevelopmentReport.md](Docs/DevelopmentReport.md) 和 [Docs/TestResults](Docs/TestResults/)。

## 常见问题

| 情况 | 处理方式 |
|---|---|
| 提示 Newtonsoft / NUnit 程序集找不到 | 先确认官方 JSON 包已解析；仅运行测试时需要 Test Framework。保留所复制目录的 asmdef，不导入第二份 Newtonsoft DLL。 |
| 字段名、类型或 ID 校验失败 | 点击错误条目定位 A1 地址，在源表修复并保存后重新扫描。类型仅支持表中列出的精确拼写。 |
| 两个工作簿包含同名表 | 给表使用不同合法名称，或移除不应参与本项目导出的来源；只选中其中一表导出也不会绕过冲突。 |
| 文件被占用、正在保存、损坏或无法读取 | 等待表格软件保存完成，检查权限和文件格式；损坏文件不会替换旧输出，其他合法表仍可处理。 |
| 已有文件没有归属记录或摘要不符 | 先备份并检查是否手改、漏拷贝记录或混入其他项目输出；恢复匹配的记录与生成物，不强制覆盖。 |
| 修改目录或移动源表后提示归属冲突 | 保留原布局继续导出，或先规划人工迁移；工具没有自动重定位/接管旧结果功能。 |
| 设置文件无法解析或归属未知 | 检查 `UserSettings/ExcelDataTool.json`，备份后修复；工具不会静默覆盖未知文件。 |
| 公式报缓存缺失 | 在 Excel/WPS/LibreOffice 中重新计算并保存，或改用普通值；导表不计算公式。 |
| 改了命名空间后示例脚本编译失败 | 同步更新可选示例对生成类型的引用，或在不需要示例的生产项目中仅安装通用工具目录。 |
| “添加示例”报告文件不存在 | 最小安装不含 GameData；添加自己的源表，或安装完整示例。 |

同步扫描大目录时编辑器可能短暂等待。读取器限制单个 ZIP 部件 32 MiB、解压总量 128 MiB、10000 个部件及 500000 个实际单元格；超限明确拒绝。加密工作簿、合并配置单元格和复杂 Excel 格式不在支持范围内。

## 许可证与项目规则

第三方版本、原始许可证来源和版权说明见 [ThirdPartyNotices.md](Docs/ThirdPartyNotices.md)。工具使用 Unity 自带 ZIP/XML API 读取工作簿，没有额外 Excel DLL。维护本工程时遵循 [AGENTS.md](AGENTS.md)。

本项目自有代码目前**未声明独立的开源许可证**；第三方组件的许可不自动适用于本项目全部代码，也不改变 Unity 官方包自身的许可。
