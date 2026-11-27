# Windows Preview 3 界面验收

本轮基于用户提供的截图调整 WPF 界面。宽屏用七列议程和按周对齐的月视图横条。日视图按标题测量卡片宽度，96–260 DIP，重叠多时可横向滚动。待安排显示一至四列。简历与面试时间表、计划分列，面经按已有题型分列，两者按窗口宽度换行。

日期选择器保留只选日期、指定时分、当前时间和取消。使用项目按钮与日期网格，操作按钮保持同行。随身日程可直接打开外观面板调节，标题、刻度和开关文字使用浅底深字衬底，工具按钮悬停或聚焦时使用实色。方案无需读取桌面内容。

使用离屏 WPF 渲染检查日、周、月、待安排、简历分栏和面经详情。示例画面使用合成数据。另检查 980 DIP 窄窗下的题型换行。日期弹窗的实际交互、桌面穿透和外观操作留待设备验收。没有操作真实日程或安装覆盖。

Release 构建成功，保留既有 WinForms 高 DPI 清单告警。NativeChecks 110 项通过，其中新增月视图相同任务对齐、重复任务状态与同名独立任务隔离。Python 回归 268 通过、1 跳过。

设计参考 [Windows 响应式布局](https://learn.microsoft.com/windows/apps/design/layout/responsive-design) 和 [Windows 文字对比度](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements)。
