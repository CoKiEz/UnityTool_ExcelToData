# 开发者指南

面向策划的填表和导表流程在 [README](../README.md)；第一次安装见 [安装说明](Installation.md)；制作发布包见 [发布指南](ReleaseGuide.md)。本页保留程序集、迁移、运行时读取和测试细节。

## 目录和依赖

| 目录 | 内容 | 是否必须安装 |
|---|---|---|
| `Assets/ExcelDataTool/Editor` | UI Toolkit、XLSX 读取、校验、导出；Editor 专用程序集 | 导表必须 |
| `Assets/ExcelDataTool/Editor/Distribution` | 维护者的发布包导出入口 | 发行包不包含 |
| `Assets/ExcelDataTool/Runtime` | 从 TextAsset 或内存字符串读取强类型 JSON | 可选，正式工具包包含 |
| `Assets/ExcelDataTool/Documentation` | 随包简明说明、MIT、第三方声明 | 发布包包含 |
| `Assets/ExcelDataTool/Tests` | Editor 测试及独立 XLSX 夹具 | 源码开发时可选 |
| `Assets/ExcelDataTool/ExportRecords` | 当前数据项目的导出归属和摘要 | 自动生成；不能随便删除 |
| `Assets/GameData/Examples` | 具体 Enemy 运行时示例、场景和示例测试 | 仅源码仓库提供 |

实际验证环境为 Unity **6000.3.17f1**。导表和可选 Runtime 使用 Unity 官方 `com.unity.nuget.newtonsoft-json 3.2.2`，对应 Newtonsoft.Json **13.0.2**。只读 OOXML 实现使用 Unity 自带 ZIP/XML API，不额外分发 Excel DLL。详细许可见 [ThirdPartyNotices](ThirdPartyNotices.md)。

Editor 与 Runtime 互相独立；通用工具不引用 Enemy、Card 或示例场景。默认生成类也不依赖本工具 Runtime 或 UnityEditor。工具目录可以移动，归属记录仍固定写入 `Assets/ExcelDataTool/ExportRecords`。

## 输出契约和保护

输入契约完整定义见 [开发需求](ExcelDataTool_DevelopmentSpec.md)。每个有效表生成 `<表名>.json`、`<表名>Row.cs`、`<表名>Table.cs`，默认命名空间 `ExcelDataTool.Generated`；JSON 根固定为 `{"rows":[...]}`，键与 C# 字段一致。数据不嵌入生成脚本。

输出为 UTF-8 确定性文本。校验失败的表不写新文件或覆盖旧结果，批量中的其他合法表可继续。文件内容一致时保留原始字节和时间戳，兼容 Git 的 CRLF/LF 转换；只修改数值不会改写无变化的 C#。

输出目录必须在当前项目 Assets 内，不能位于 Editor，也不接受 `.` / `..` 路径段或符号链接绕过。覆盖前检查自动生成标记、归属记录、源身份和内容摘要；已有手写文件、手改输出和丢失记录都会阻止覆盖。内部归属记录不计入界面“写入/跳过”文件数。

单表普通写入异常执行回滚，多表之间不是整体事务。断电/强制终止时需检查输出及可能保留的备份。过时文件仅提示，不自动清理；不提供强制接管未知文件的按钮。

## 迁移已有数据

- **新数据项目：**安装工具，复制源表，再导出。不要带旧的生成物、ExportRecords 或 UserSettings。
- **延续已有数据：**同时保留源表、生成 JSON/C#、对应 ExportRecords 和 `.meta`，保持工程内相对路径和内容；首次迁移沿用原输出目录及命名空间，再校验和导出。
- **改变已有源位置或输出目录：**先规划人工迁移与代码引用更新；工具会阻止静默接管旧结果。不要删未知文件来绕过检查。

工程内源身份使用相对路径；工程外源身份是规范化路径的摘要，换机器或位置可能触发保护。跨操作系统路径大小写差异尚未验证。窗口设置保存在本项目 `UserSettings/ExcelDataTool.json`，目标项目应重新配置，不替换其整个 ProjectSettings 或 manifest。

## 运行时读取

`ExcelDataTool.Runtime.JsonTableLoader.Load<TTable>(TextAsset)` 从 Inspector、Prefab 或项目资源加载系统提供的 TextAsset 还原生成的表容器。已有 JSON 字符串使用 `LoadJson<TTable>(string)`；不在 Player 中通过 `Assets/...` 磁盘路径读取文件。

读取器验证根对象及 `rows` 数组，拒绝未知 JSON 成员、重复成员及无法转换的值，并显式禁用类型元数据激活；它不替代编辑器的数据校验，也不会补全缺失配置。修改表结构时同步生成 C# 和 JSON。

源码中的 `GameData.Examples.EnemyConfigExample` 演示读取 `EnemyTable` 并查找 `id == 1001`。打开 `Assets/GameData/Examples/Scenes/EnemyConfigExample.unity`，检查 Enemy.json 引用，进入 Play Mode 查看 Console。这个具体示例固定使用默认命名空间和 Enemy 字段；改变它们时需同步修改示例脚本。常规工具/模板发行包不包含该示例，避免给目标项目引入具体业务类型。

示例生成程序集为 `GameData.Generated`，示例行为为 `GameData.Examples`。`Assets/GameData/Examples/link.xml` 为前者保留反射读取的类型和字段；自己的项目更换程序集或裁剪设置时应设置相应规则并实际构建。参考 [Unity 程序集定义](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/script-compilation/assembly-definition-files/file-format) 和 [代码裁剪说明](https://docs.unity.cn/Manual/ManagedCodeStripping.html)。

实际通过的 Player 验证为 Windows x64 Mono；IL2CPP、移动端及其他平台未验证。读取后的对象是静态配置的内存副本，游戏 HP、金币和存档需要单独管理，不回写配置 JSON 或源表。

## 源码测试

安装 `com.unity.test-framework 1.6.0`，在 **Window → General → Test Runner → EditMode** 运行对应程序集：

| 测试目录 | 所需正式代码/资源 |
|---|---|
| `Tests/Editor` + 相邻 `Tests/Fixtures` | 仅 Editor；不需要 GameData 或 Runtime |
| `Tests/Runtime` | Runtime；不需要 GameData |
| `GameData/Examples/Tests` | Runtime、生成 Enemy 类型、示例行为及真实 Enemy.json |

固定夹具通过测试脚本编译位置定位，移动插件目录时保持 Fixtures 与 Editor 的相邻关系。测试修改和导出发生在工程 Temp 下独立目录，不修改源夹具和使用者的示例。测试程序集均限定 Editor；不用 Test Framework 的项目不复制测试目录。

干净迁移工程与原工程副本的 110 项回归、Player 构建及运行记录见 [开发报告](DevelopmentReport.md) 和 [测试证据](TestResults/)。发布包自身的导入检查单独记录，不能用源码测试代替包验证。

## 已知边界

手动触发同步扫描，不监听 Excel；大目录可能有等待。预览最多 100 条非空数据和 30 列，限制不影响合法数据的完整导出。公式只读取保存的缓存，无法判断是否过时。日期、数组、字典、嵌套字段、加密工作簿和合并配置单元格不支持。

读取器限制单个 ZIP 部件 32 MiB、解压总量 128 MiB、10000 个部件及 500000 个实际单元格，超限明确拒绝。
