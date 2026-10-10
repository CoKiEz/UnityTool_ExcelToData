# 安装说明：第一次使用时完成一次

这页适合负责给项目安装工具的人。策划同事如果已经能看到 **Tools → Excel Data Tool** 菜单，可以直接回到 [使用手册](../README.md)，从准备 Excel 开始。

## 需要准备什么

- 一个 Unity 6 项目。实际验证版本为 **Unity 6.3 LTS / 6000.3.17f1，Windows**；其他 Unity 6 版本以各项目实际编译为准。
- 能联网安装官方 JSON 依赖的 Unity Package Manager。
- 发布后的工具安装包；源代码安装方式见本页末尾。

工具本身不需要安装 Excel、Python 或 Office。编辑表格时可以使用 Excel、WPS 等支持 `.xlsx` 的软件。

## 第一步：下载工具包

打开仓库的 [Releases 页面](https://github.com/CoKiEz/UnityTool_ExcelToData/releases)，在对应版本下展开 **Assets**：

| 文件 | 用途 | 是否需要 |
|---|---|---|
| `ExcelDataTool-1.0.0.unitypackage` | 导表窗口、运行时读取辅助类、简明说明和许可证 | 需要 |
| `ExcelDataTool-Templates-1.0.0.unitypackage` | Enemy、Card 两份 Excel 模板和 MIT 许可 | 第一次练习时推荐 |
| `SHA256SUMS` | 校验下载的两个包是否完整 | 可选 |
| `ReleaseNotes.md` | 版本说明、依赖和已执行验证 | 推荐阅读 |

**这些是待发布版本的文件名。若 Releases 尚未出现对应版本，请使用源码安装方式或等待维护者发布。** GitHub 自动提供的 `Source code (zip)` 是整个源码仓库，不是可直接通过 Unity 导入的插件包。

## 第二步：安装 JSON 依赖

1. 用 Unity Hub 打开准备使用的项目，等待 Unity 编辑器打开。
2. 在编辑器顶部选择 **Window → Package Management → Package Manager**。不同 Unity 6 小版本也可能直接显示 **Window → Package Manager**。
3. 点击左上角 **＋**，选择 **Install package by name…**（部分版本显示 Add package by name）。
4. 在 **Name** 填 `com.unity.nuget.newtonsoft-json`，在 **Version** 填 `3.2.2`，点击 **Install**。
5. 等待安装和编译结束。已有此官方包的项目不必重复安装，也不要额外拖入 Newtonsoft DLL。

`.unitypackage` 只导入 Assets 资源，**不会替你安装这个 Package Manager 依赖**，所以应先完成本步。操作位置参见 [Unity Package Manager 文档](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-ui-access.html)。

## 第三步：导入工具和模板

1. 选择顶部 **Assets → Import Package → Custom Package…**。
2. 选择下载的 `ExcelDataTool-1.0.0.unitypackage`。
3. 在导入列表中查看内容，点击右下角 **Import**，等待编译完成。
4. 如需模板，以同样方式导入 `ExcelDataTool-Templates-1.0.0.unitypackage`。
5. 打开 **Tools → Excel Data Tool**。导入过模板后，点击 **添加示例**，应看到 Enemy、Card、Rarity 三张有效表。

如果目标项目已经有自己的 Enemy.xlsx 或 Card.xlsx，导入模板前查看路径；取消同路径文件的勾选，或先在空项目练习，避免替换业务源表。工具包不带现有生成结果和归属记录；模板第一次导出会在目标项目创建它们。

导入步骤依据 [Unity 本地资源包导入说明](https://docs.unity3d.com/6000.3/Documentation/Manual/AssetPackagesImport.html)。不需要点击顶部三角形 Play 按钮来导表。

## 第四步：向使用者交接

安装者确认好源表位置、输出目录和命名空间，然后把 [使用手册](../README.md) 交给策划。默认结果位于：

```text
Assets/GameData/Generated/Json/    游戏读取的数据
Assets/GameData/Generated/Code/    与数据配套的 C# 类型
Assets/ExcelDataTool/ExportRecords/    工具维护的导出记录
```

日常只需要改 Excel 数据并重新导出。字段名、类型、输出目录或命名空间的变更需要与使用这些数据的程序代码同步。

## 更新已有工具

导入新版本工具包前备份或提交项目的当前修改，再核对导入列表。Unity 的包导入可能替换同路径或同 GUID 资产；导表器的“防止覆盖手写文件”只约束导表输出，不代替 Unity 包导入的检查。若你修改过工具源代码，应先比较差异。

普通升级只导入工具包；不要为了升级重导模板，更不要删除 `ExportRecords`、业务源表或既有输出。发现同名程序集、重复工具目录或 Newtonsoft 冲突时，请让项目维护者检查，不要通过随意删 DLL 解决。

## 从源码安装（没有发布包时）

1. 安装上文官方 JSON 依赖。
2. 从源码仓库复制 `Assets/ExcelDataTool/Editor/`、`Runtime/`、`Documentation/`，保留目录及文件的 `.meta`；`Editor/Distribution` 是维护者的打包工具，可以不复制。
3. 不复制 `Tests`、`ExportRecords`、`ProjectSettings`、`Library` 或整个 `Packages/manifest.json`。
4. 如需练习，单独复制 `Assets/GameData/Excel/Enemy.xlsx`、`Card.xlsx` 及 `.meta`，通过导表工具创建新结果。

运行时示例、源码测试及延续既有数据项目的迁移细节见 [开发者指南](DeveloperGuide.md)。
