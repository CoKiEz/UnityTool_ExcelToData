# 第三方依赖与许可证记录

本文依据当前工程实际解析的包及其随附许可文件整理。包由 Unity Package Manager 获取，工具未在 `Assets` 中另行捆绑第三方 DLL，也未更改第三方包的许可。包升级后应重新核对其 `package.json`、`LICENSE.md` 和 `Third Party Notices.md`。

## 必需依赖：Unity Newtonsoft JSON

| 项目 | 实际值 |
|---|---|
| Unity 包 | `com.unity.nuget.newtonsoft-json` |
| 包版本 | `3.2.2` |
| Newtonsoft.Json 上游版本 | `13.0.2`，由包内 `package.json` 明确声明 |
| 获取来源 | Unity Registry，`https://packages.unity.com`，版本锁定见 `Packages/manifest.json` 和 `Packages/packages-lock.json` |
| Unity 包装层许可 | Unity Companion License for Unity-dependent projects |
| 第三方组件许可 | MIT，具体组件和版权人见下文 |

核对来源为 `Library/PackageCache/com.unity.nuget.newtonsoft-json@4dfd81071c64/` 中的 `package.json`、`LICENSE.md` 和 `Third Party Notices.md`。缓存目录后缀可能随 Unity 的解析方式改变；也可以从 Unity 的 `Packages/com.unity.nuget.newtonsoft-json` 虚拟包目录查看这些文件。[官方包说明](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html) 同样注明对应 Newtonsoft.Json 13.0.2。

包中 Unity 部分的原始版权声明为 `Nuget.Newtonsoft.Json copyright © 2022 Unity Technologies ApS`；适用 [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license)。这不意味着整个 Unity 包被重新授予 MIT 许可。

随包的第三方清单包含：

| 组件 | 随包记录的版权人 | 许可 |
|---|---|---|
| Newtonsoft.Json | Copyright (c) 2007 James Newton-King | MIT |
| Json.Net.Unity3D | Copyright (c) Copyright (c) 2016 SaladLab | MIT |
| Newtonsoft.Json-for-Unity | Copyright (c) Copyright (c) 2019 Kalle Jillheden (jilleJr) | MIT |
| com.newtonsoft.json | Copyright (c) 2019 Mike Wuetherick | MIT |

上述版权文本保留包内原样，包括其重复的 `Copyright (c)`。完整第三方通知由原包随附；下方保留共同使用的 MIT 授权文本，与本表各组件的对应版权声明一起阅读。

```text
The MIT License (MIT)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

## 可选测试依赖

编辑器测试使用工程已有的 Unity Test Framework，不属于只安装导表工具时的额外运行时依赖。

| 包 | 本工程解析版本 | 包装层许可 | 第三方说明 |
|---|---|---|---|
| `com.unity.test-framework` | `1.6.0` | Unity Companion License | 原包 `LICENSE.md`：Copyright © 2024 Unity Technologies ApS |
| `com.unity.ext.nunit` | `2.0.5` | Unity Package Distribution License | 基于 NUnit 3.5；其 NUnit 部分为 MIT |

具体核对文件是解析后的上述包内 `package.json`、`LICENSE.md`，以及 `com.unity.ext.nunit/Third Party Notices.md`。Test Framework 声明的依赖下限可能与整个工程最终解析版本不同，实际版本以 `Packages/packages-lock.json` 为准。

Custom NUnit 包的原始版权记录为 `Custom Nunit copyright © 2019 Unity Technologies`；其 `LICENSE.md` 指向 [Unity Package Distribution License](https://unity3d.com/legal/licenses/Unity_Package_Distribution_License)。第三方通知记录 `Nunit 3.5`、`Copyright © 2018 Charlie Poole, Rob Prouse` 和 MIT 授权，MIT 正文与上方相同。使用或分发这些包时保留原包随附通知；本文不替换它们。

## Excel 解析与开发工具

Excel 读取器是本项目维护的受限 OOXML 只读实现，使用 Unity 所提供的 .NET `System.IO.Compression`、`System.Xml`、`System.Xml.Linq` API。没有随工具分发额外的 Excel 解析库或 Office 组件。Unity 引擎、编辑器及随附框架仍适用其自身许可。

示例制作过程中可以使用表格软件或开发机器上的 openpyxl；这些工具不是导表器的依赖，用户运行、迁移和测试本工具不需要安装 Python/openpyxl。

## 本项目代码

本仓库目前没有为自有工具代码声明独立开源许可证。上述 MIT、Unity Companion License 和 Unity Package Distribution License 只适用于相应组件，不能据此认定整个 `UnityTool_ExcelToData` 仓库采用其中任何一种许可证。
