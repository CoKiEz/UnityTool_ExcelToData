# 发布维护指南

本文面向维护者，说明如何从源码制作可下载的 Unity 导入包，并发布到 GitHub Releases。使用者的安装步骤见 [Installation.md](Installation.md)，代码结构和开发验证见 [DeveloperGuide.md](DeveloperGuide.md)。当前发布版本为 **1.0.0**。

## 开源仓库与许可

本项目自有代码采用根目录 [LICENSE](../LICENSE) 中的 MIT 许可，版权声明为 `Copyright (c) 2026 CoKiEz`。发布源码、修改版本或导入包时保留该版权与许可文本；README 应明确说明项目许可，仓库根目录保留完整 LICENSE。GitHub 将公开仓库和开源许可分别处理，公开仓库仍应带有明确的许可文件。[GitHub：为仓库添加许可](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository)。

项目的 MIT 不替代第三方组件的许可。必需依赖 `com.unity.nuget.newtonsoft-json 3.2.2` 的 Unity 包装层适用 Unity Companion License，其中 Newtonsoft.Json 等第三方部分有各自 MIT 声明。包内不捆绑第三方 DLL，完整版本和许可说明见 [ThirdPartyNotices.md](ThirdPartyNotices.md)。维护者更新依赖后，应同步核对原包的 `package.json`、`LICENSE.md` 和 `Third Party Notices.md`。

公开源码仓库可以保留用于开发和复现测试的 Unity 工程文件，包括 `Assets`、`Packages`、`ProjectSettings` 和文档。提交前检查实际差异，保持 `.meta` 与对应资源一起提交；`Library`、`Temp`、`Logs`、`.utmp`、`UserSettings` 及本地构建产物不进入源码提交。

版本包在本地 `Releases/` 下生成并由 Git 忽略，推荐上传为 GitHub Release 附件，不把每一版二进制反复提交到源码历史。给使用者的插件下载入口应指向 `.unitypackage`，不要将 `Library` 或整个 Unity 工程压缩包作为插件安装包。

## 两个包的边界

本版本在 `Releases/1.0.0/` 下准备以下四项发布文件：

| 文件 | 内容与用途 |
|---|---|
| `ExcelDataTool-1.0.0.unitypackage` | 通用 Editor、Runtime，以及 Documentation 中的安装说明、MIT 许可和第三方通知 |
| `ExcelDataTool-Templates-1.0.0.unitypackage` | 可选 Enemy.xlsx、Card.xlsx 表格模板，以及与主包相同的 MIT 许可文件 |
| `SHA256SUMS` | 两个最终 `.unitypackage` 的 SHA-256 和文件名，用于下载后校验 |
| `ReleaseNotes.md` | 当前版本的功能、依赖、验证结果、限制及更新说明 |

核心包采用逐文件允许列表，包含 `Assets/ExcelDataTool/Editor/` 中的功能代码、`Runtime/`，以及 `Documentation/README.md`、`Documentation/LICENSE.txt`、`Documentation/ThirdPartyNotices.md`。用于制作发行包的 `Editor/Distribution/` 不随包分发；发布菜单只在完整源码工程中使用。

两个包均不携带 Tests、测试夹具、ExportRecords、用户设置、生成的 JSON/C#、示例场景、示例组件、第三方 DLL 或工程配置。模板包只提供两份工作簿和许可，不会预先导入 `EnemyRow` 等生成类型。第一次导入模板时，由目标工程通过正式导表流程创建自己的生成物与归属记录。

使用者必须先通过 Package Manager 安装官方 **`com.unity.nuget.newtonsoft-json 3.2.2`**。本发行物是传统 `.unitypackage`，没有 UPM `package.json` 依赖声明，也不会自动安装此依赖；不要用开发工程的 `Packages/manifest.json` 覆盖使用者的文件。按名称添加包的操作见 [Unity：从注册表按名称安装包](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-ui-quick.html)。

## 生成发行包

1. 用 **Unity 6000.3.17f1** 打开完整源码工程，确认官方 JSON 依赖已解析且 Console 无编译错误。核对根 LICENSE、第三方通知和包内 Documentation 副本一致。
2. 检查 `ExcelDataToolPackageExporter` 中的 `Version` 与本次发布版本、目录和说明一致。首次为 `1.0.0`；以后更新版本时同时检查默认输出目录和允许列表，新增功能文件不能依赖目录递归被自动带入。
3. 执行 **Tools → Export Excel Data Tool Packages**，生成两个 `.unitypackage`。命令行入口见下方。
4. 在干净工程实际安装两个包并验证后，编写对应 `ReleaseNotes.md`，再为最终包计算 `SHA256SUMS`。导出器只生成两个包，不自动生成发布说明或摘要文件。

源码入口为 `ExcelDataTool.Editor.Distribution.ExcelDataToolPackageExporter`：

| 接口 | 行为 |
|---|---|
| `Version` | 当前版本常量 `1.0.0`，用于包名 |
| `ExportReleasePackages(string outputDirectory = "Releases/1.0.0")` | 返回两个包的路径；自定义输出目录必须仍位于当前工程的 `Releases` 内 |
| `ExportBatch()` | 使用默认输出位置的无参数批处理入口，没有自定义命令行参数 |

如果任一同名包已经存在，导出器会在写入前拒绝覆盖。正式新版本使用新的版本目录；同版本重建时，维护者先保存或核对已有候选包，再整理专用输出目录，不让脚本删除未知文件。

可在未被另一个 Unity 进程打开的发布副本中运行以下命令；将 Unity 可执行文件和路径替换为实际位置：

```text
Unity.exe -batchmode -nographics -quit -projectPath "<发布副本绝对路径>" -executeMethod ExcelDataTool.Editor.Distribution.ExcelDataToolPackageExporter.ExportBatch -logFile "<工程内发布日志绝对路径>"
```

`ExportBatch` 完成后不自行关闭编辑器，因此命令需要 `-quit`。失败时检查退出码和日志，不能只因目录中出现文件就认为打包成功。Unity 对批处理静态入口的要求见 [Editor 命令行参数](https://docs.unity3d.com/6000.3/Documentation/Manual/EditorCommandLineArguments.html)。

导出使用 Unity 的 [AssetDatabase.ExportPackage](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetDatabase.ExportPackage.html)。维护允许列表时不要开启自动包含依赖或对整个 Assets 递归导出；否则可能把用户生成物或其他程序集一起带入发行物。实际包清单需要在生成后复核，不能仅凭源码列表推断归档内容。

最终包确定后，可在源码工程根目录用 PowerShell 生成摘要文件：

```powershell
# 摘要针对最终发行包字节；任一包重新生成后都需要重新计算。
$releaseDirectory = Join-Path (Get-Location) 'Releases/1.0.0'
$releaseNames = @(
    'ExcelDataTool-1.0.0.unitypackage',
    'ExcelDataTool-Templates-1.0.0.unitypackage'
)
$releaseChecksums = foreach ($releaseName in $releaseNames) {
    $releaseHash = Get-FileHash -LiteralPath (Join-Path $releaseDirectory $releaseName) -Algorithm SHA256
    '{0}  {1}' -f $releaseHash.Hash.ToLowerInvariant(), $releaseName
}
[System.IO.File]::WriteAllLines(
    (Join-Path $releaseDirectory 'SHA256SUMS'),
    [string[]]$releaseChecksums,
    (New-Object System.Text.UTF8Encoding($false))
)
```

## 验证与版本证据

发布包和源码提交应对应同一个版本。发布前保留以下实际证据，并在发布说明中区分 `PASS`、`FAIL`、`NOT RUN`：

1. 对本次源码运行适用的自动测试，记录 Unity 版本、测试数量、退出码和 NUnit XML；同时检查许可、文档和包清单。
2. 在干净 Unity 6.3 工程先安装官方 JSON 包，再导入核心包，确认能编译并打开 UI Toolkit 窗口。目标工程不需要 Test Framework、既有 GameData 或生成类型。
3. 导入可选模板，扫描并导出 Enemy、Card、Rarity 三张表；确认首次生成 9 个 JSON/C#，再次导出为 0 写入、9 跳过；再确认非法输入不会覆盖旧结果。
4. 对已有工具和用户数据的副本检查升级行为，确认没有带入来源不同的 ExportRecords、模板覆盖或重复程序集；运行时、Player 或其他平台只记录本次实际执行的范围。

源码工程第三阶段已有真实验证记录，包括工具单独迁移 **107/107**、含示例 **110/110** EditMode，以及 Windows x64 Mono Player 构建与运行。这些证明对应版本的功能，不能替代新制作 `.unitypackage` 的导入验证。引用证据时注明所属提交与验证范围：[开发报告](DevelopmentReport.md)、[工具独立迁移 XML](TestResults/StageThree-ToolOnly-EditMode.xml)、[含示例回归 XML](TestResults/StageThree-EditMode.xml)、[第三阶段验证记录](TestResults/StageThree-Verification.json)。

## 发布到 GitHub

由维护者在审阅源码差异、许可与发布证据后提交发布源码，并将其放入公开 GitHub 仓库。首次标签建议 **`v1.0.0`**，标签指向实际用于生成和验证包的提交。不要移动已发布版本的标签来替换新代码；修复发布采用新的版本号。

在仓库的 **Releases → Draft a new release** 中选定标签，填写标题和当前发布说明，附上两个 `.unitypackage`、`SHA256SUMS`、`ReleaseNotes.md` 四项文件。先保存草稿核对下载文件、依赖说明与证据链接，最后由维护者发布。GitHub 支持在 Release 中附加二进制资源；发布界面及草稿操作以 [GitHub 官方 Release 指南](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository) 为准。

在 GitHub Release 正文中，把文档和测试证据链接改为仓库对应标签下的完整链接，避免相对链接指向错误位置。GitHub 自动提供的 Source code 归档用于下载源码；推荐的直接安装附件仍是这里生成的两个 `.unitypackage`。

本地生成包不代表已经发布到 GitHub。打包菜单和批处理入口不执行 `git commit`、`git tag`、`git push`、创建远程仓库或发布 Release；这些操作由维护者明确执行。

## 更新已有用户工程

升级核心包前，让使用者保留项目版本记录或备份，并检查 Unity 导入列表。官方更新可能覆盖目标位置的工具源码，因此本地自定义的工具修改应先比较、合并；保持原 `.meta` GUID，不同时安装两份相同程序集。

模板包是可选的初次示例输入，已经修改过 Enemy.xlsx 或 Card.xlsx 的工程不要直接重新导入同路径模板。Unity 包导入不经过导表器的文件所有权检查，不能依靠导出保护阻止导入界面覆盖用户的 Excel。

`Assets/ExcelDataTool/ExportRecords`、已有生成物和 `UserSettings` 都不由发行包替换。升级工具时保留它们；不要删除或编辑归属记录来绕过冲突。版本若改变表格协议、输出布局、命名空间约定或记录格式，应在发布说明中给出迁移步骤并先验证旧数据副本，不能静默接管自定义文件。

## ReleaseNotes.md 模板

下面是需要填写的模板，不是已完成验证的声明。只保留实际执行的结果，并把占位项替换为当前版本的信息。

```markdown
# Excel Data Tool 1.0.0

- 标签：v1.0.0
- 源码提交：<实际提交 SHA>
- 许可：MIT，Copyright (c) 2026 CoKiEz；第三方依赖继续遵守原许可。
- 支持环境：Unity 6.3 LTS；本次验证编辑器版本 <实际版本>。
- 必需依赖：通过 Package Manager 预先安装 com.unity.nuget.newtonsoft-json 3.2.2。

## 下载与安装

- ExcelDataTool-1.0.0.unitypackage：核心工具和文档。
- ExcelDataTool-Templates-1.0.0.unitypackage：可选工作簿模板和 MIT 许可。
- SHA256SUMS：本次两个包的校验值。
- 安装指南：<当前标签下 Docs/Installation.md 的完整链接>。

## 本版变化

<说明实际交付的功能、修复及影响旧工程的变化。>

## 验证

- <PASS / FAIL / NOT RUN>：本次源码测试，<数量、Unity 版本、原始 XML 链接>。
- <PASS / FAIL / NOT RUN>：本次两个包的干净工程导入、三表首次导出及重复导出。
- <PASS / FAIL / NOT RUN>：已有工程更新及旧生成结果保护。
- <PASS / FAIL / NOT RUN>：运行时或 Player 验证，<平台、后端、结果链接>。
- 开发报告：<当前标签下 Docs/DevelopmentReport.md 的完整链接>。

## 更新与限制

<列出本版更新步骤、已知问题和未测试的平台。>
保留自定义工具修改、用户源表、生成物和 ExportRecords；不要用模板覆盖已编辑工作簿。
```
