# Windows 原生 Demo 1.1-W

独立于旧 WebView2 客户端，WPF/.NET 10 原生业务页面，示例数据与正式账号隔离。

## 安装和启动

解压 `AwesomeInterview-NativeDemo-1.1-W-win-x64.zip` 后运行 `Install.cmd`。安装到当前用户的 `%LOCALAPPDATA%\Programs\AwesomeInterviewNativeDemo`，并创建开始菜单快捷方式。不需要管理员权限，也不需要安装 .NET SDK。可直接运行压缩包中 `app\AwesomeInterview.NativeDemo.exe` 进行便携测试。

从开始菜单打开 Awesome Interview Native Demo。卸载前关闭程序，再运行安装目录的 `Uninstall.cmd`，输入 YES 确认。默认保留 `%LOCALAPPDATA%\AwesomeInterviewNativeDemo` 下的演示数据；需要清理时可自行删除该目录。当前未签名，遇到系统阻止请保留提示并反馈，不建议关闭系统防护。

## 可用范围

今日待办、完成/恢复、日历选日、添加标题/日期/备注、本机JSON持久化、面经示例、版本说明。暂无真实登录/同步/提醒、完整周月时间轴、时分区间和导出。本轮不连接公开站点。

## 构建

```powershell
dotnet publish windows/AwesomeInterview.NativeDemo/AwesomeInterview.NativeDemo.csproj -c Release -r win-x64 --self-contained true -o windows/NativeDemo-package/app
Copy-Item windows/installer/*.cmd windows/NativeDemo-package/
Compress-Archive -Path windows/NativeDemo-package/* -DestinationPath windows/publish/AwesomeInterview-NativeDemo-1.1-W-win-x64.zip -Force
```

PRD 位于本机忽略目录 `docs/APP-PRD-1.1-Windows原生版.md`，独立版本号 1.1-W。原生 Demo 后续先拆分 MVVM 与 API/缓存服务，再接入正式数据。

本轮验收：SDK 10.0.403 Release 自包含发布成功；安装脚本已实际安装并创建开始菜单快捷方式；原生窗口和控件经 UI Automation 树确认。窗口截图/点击工具在当前环境超时，交互、卸载、干净机器和多DPI仍待验收。

## 1.1.1-W 外观与清晰度更新（2026-11-04）

启用 PerMonitorV2 DPI manifest、UseLayoutRounding、SnapsToDevicePixels、Display 文字格式与 ClearType。改为浅色侧栏、蓝色选中态、圆角按钮与输入框，统一字体、留白及卡片边框，接入项目logo。构建和覆盖安装、启动通过；当前窗口截图工具超时，多DPI与视觉清晰度仍需实机确认。安装包版本为1.1.1-W，沿用原数据目录。


## 1.2.0-W 桌面流程与随身日程（2026-11-05）

- 默认关闭主窗口到托盘，首次说明如何恢复和退出；设置可关闭，设置文件 desktop-settings.json 保存在当前用户演示目录。
- 托盘右键：打开主窗口、开启/关闭随身日程、设置、退出应用；完全退出会关闭组件并释放托盘图标。
- 随身日程：24小时时间轴、当日任务、当日已完成、完成/恢复、详情卡片。旧任务没有起止时间的只显示列表；新任务支持当天HH:mm起止时间，跨天时间区间尚未接入。
- 新增未保存表单关闭/侧栏导航确认；本地任务保存失败时完成状态回滚。
- 设置中的用户名只是演示昵称，不修改正式账号；真实账号、同步、通知、透明/穿透仍未实现。
- 构建、覆盖安装、启动及控件树已确认；当前自动化工具点击仍缺少几何信息，托盘/组件交互、设置重启持久化和多屏仍需实测。
- 构建含WFO0003提醒：由于WPF项目使用WinForms的NotifyIcon，分析器提示WinForms DPI配置；当前仍采用WPF的PerMonitorV2 manifest，未切换到WinForms应用入口。
