# Excel Data Tool 1.0.0：快速使用

本工具把按约定填写的 Excel 转成游戏使用的 JSON 数据和配套 C# 类型。完整的策划使用手册、图片说明及安装文档见 [项目主页](https://github.com/CoKiEz/UnityTool_ExcelToData)。

## 初次安装

使用 Unity 6；实际验证环境为 Windows / Unity 6.3 LTS 6000.3.17f1。先通过 Package Manager 安装官方 `com.unity.nuget.newtonsoft-json` **3.2.2**，再通过 **Assets → Import Package → Custom Package…** 导入工具包。本包不会自动安装该依赖，不含第三方 DLL。

可选模板包提供 `Assets/GameData/Excel/Enemy.xlsx`、`Card.xlsx`，不带任何现有导出结果、场景或测试依赖。目标已有同名源表时，导入前先核对并取消相应勾选，不替换业务数据。

## 先填写 Excel

1. 保存为 `.xlsx`。底部工作表标签使用 `Enemy`、`Card` 等英文名称，首字母大写；不同配置表不能同名。
2. 从 A1 开始：第 1 行填英文小写开头的字段名；第 2 行填类型；第 3 行填中文说明；第 4 行起填数据。
3. 每张表必须有 `id` 列，类型 `int`，同表每条记录的 id 不重复。

| Excel 行 | A | B | C |
|---|---|---|---|
| 1 | id | name | maxHealth |
| 2 | int | string | int |
| 3 | 唯一编号 | 敌人名称 | 最大生命值 |
| 4 | 1001 | 哥布林 | 120 |
| 5 | 1002 | 骷髅兵 | 150 |

“Excel 行”和 A/B/C 是此说明的坐标标注，不要作为额外行列复制进数据。

支持 `int`（整数）、`float` / `double`（小数）、`bool`（true/false）、`string`（文字）。类型精确小写，小数使用英文点；文字可空，其他类型不能为空。不要合并单元格、在表头上方加标题或在数据下方加合计。以 `#` 开头的工作表如 `#说明` 会忽略。

## 再导出

1. 保存 Excel。在 Unity 顶部打开 **Tools → Excel Data Tool**，不需要进入 Play Mode。
2. 点击 **添加文件…** 选择工作簿，或在安装模板后点击 **添加示例**。
3. 点击 **扫描并校验**，选择表查看预览；有错误时点击条目，回 Excel 修改对应单元格并保存。
4. 点击 **导出当前表**，或用 **批量导出** 处理来源中的全部表。
5. 检查摘要。默认结果在 `Assets/GameData/Generated/Json` 和 `Code`；不要手改生成结果或删除 `Assets/ExcelDataTool/ExportRecords`。

改数值后重新保存、导出即可；没变化的文件会显示“跳过”。批量中的错误表保留旧结果，其他合法表仍可能成功，因此必须检查失败表数。输出路径、字段结构和命名空间先与项目程序确认。

## 许可

本项目自有代码、文档和模板采用 **MIT**，版权为 **2026 CoKiEz**，完整条款见同目录 `LICENSE.txt`。第三方 Unity/JSON 包保留自身许可，见 `ThirdPartyNotices.md`。允许商用、修改和再分发时仍须保留相应版权及许可声明。
