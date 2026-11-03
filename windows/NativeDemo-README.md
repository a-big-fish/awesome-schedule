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
