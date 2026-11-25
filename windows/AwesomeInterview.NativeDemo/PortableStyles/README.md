# 随身日程样式

在主窗口的「设置与版本 → 随身日程外观」点击「打开目录」。用记事本编辑或复制一个 JSON 文件，保存后在设置里选择该样式并点「重新读取」。也可用「导入 JSON」复制并立即应用外部文件。应用后，设置会保存到当前 Windows 用户。

示例文件为 `soft-silhouette.json`、`clear-outline.json` 和 `frosted-glass.json`。复制时请换一个文件名，并把 `Name` 改为想在应用中看到的名称。格式支持 UTF-8、行注释和末尾逗号。

| 字段 | 含义 |
| --- | --- |
| `Version` | 格式版本，填 `1` |
| `Name` | 样式名称，1–40 字符 |
| `OverallTransparency`、`TextTransparency` | 整体和字体透明度，0–90 的整数 |
| `BackgroundTransparency`、`BorderTransparency` | 底色和普通描边透明度，0–100 的整数 |
| `Style.TextColor`、`Style.MutedTextColor` | 正文和次要文字的基础色。背景自适应仍会调整实际字色 |
| `Style.AccentColor` | 时间数字、时间线和选中按钮强调色 |
| `Style.SectionColor`、`Style.EventColor`、`Style.OutlineColor` | 区块底色、未完成日程卡片底色和描边色。底色透明度为 100 时底色不可见 |
| `Style.GlassStrength` | 局部磨砂强度，0–85 的整数。设为 0 可关闭 |
| `Style.GlassRadius` | 磨砂平滑半径，2–22 DIP |
| `Style.GlassFeather` | 磨砂在文字周围的扩展和渐隐距离，3–24 DIP |
| `Style.GlassTone` | `dark` 为深玻璃白字，`light` 为浅玻璃黑字，`auto` 按整块玻璃的背景明度选择。内置磨砂预设选 `auto`，自定义文件省略此项时为 `dark` |
| `Style.GlassRefraction` | 边缘最大取样偏移，0–12 DIP。设为 0 可关闭折射，省略时为 6 |

颜色用 `#RRGGBB`。整个 `Style` 可省略，省略的颜色沿用内置色，磨砂参数默认依次为 0、8、10。把 `GlassStrength` 设为大于 0 可给自定义样式启用磨砂。四个透明度字段要保留。
