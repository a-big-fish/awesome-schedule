# Awesome Schedule for Windows

| 版本 | 工程与说明 |
| --- | --- |
| **原生预览 1.6.0-W Preview 31** | WPF／.NET 10，含日历、面经、面试、简历和随身日程。见 [原生预览安装与构建](NativeDemo-README.md) 和 [版本记录](NativeDemo-HISTORY.md) |
| **WebView2 测试版 1.0-W** | 工程为 `AwesomeInterview.Desktop`，使用下方的服务端地址配置和构建命令 |

原生预览的完整发布验收仍在进行。[原生验收记录](acceptance/2026-11-21/README.md)

## 下载与更新

[下载 Windows 版](https://schedule.hunaochufang.xyz/download/windows)会显示当前发布版本。Preview 31 以 ZIP 提供，解压后运行 `Install.cmd`。首个签名的 Velopack 版本发布后，同一入口会提供 EXE 安装程序。

从 ZIP 预览版迁移前，通过系统托盘菜单退出旧应用，再使用同一 Windows 账号运行 EXE。新版沿用原有的账号缓存、面经草稿、离线同步队列和随身日程外观目录。打开新版的「设置与版本 → 关于与版本」后，可查看当前版本、检查更新和阅读更新说明。选择「下载并重启」后，程序会在后台下载，保存本机数据后安装并重开。需要继续编辑时，可以先保存内容，再回到设置完成重启。

Windows 与 Web 共用源码仓库，Windows 独立编号和打包。维护者见 [Windows 发布流程](WINDOWS-RELEASE.md)。

## WebView2 测试版 1.0-W

Windows 10/11 x64 测试版。运行 `AwesomeInterview.exe` 前，先设置自己的 HTTPS 服务地址。首次在线登录后，应用会同步个人日程与面经；服务器暂时不可用时可查看最近一次同步的只读副本。

### 使用

- 在 PowerShell 中设置 `$env:AWESOME_INTERVIEW_SERVER_URL = 'https://your-domain.example'`，再从同一个终端启动程序。不设置或地址不合法时，客户端使用不可用的 `https://example.invalid`，不会连接作者的服务。需要安装 WebView2 Runtime。
- 在网页版为单条日程勾选提醒。Windows 客户端同步后尝试登记系统计划通知；若系统不支持，则窗口关闭后保持托盘运行以发送运行时提醒。点击托盘图标可重新打开窗口，通过托盘菜单“退出程序”可完全退出。
- 点击“立即同步”可马上刷新本地快照；应用运行中也会定期同步。离线内容可能落后于其他设备，请看左下角最近同步时间。
- “退出登录”会清除本机快照、会话及待触发提醒。解压目录可直接删除；个人缓存位于当前 Windows 用户的 `%LOCALAPPDATA%\AwesomeInterview`。

### 构建

安装 .NET 10 SDK 后，在仓库根目录运行：

```powershell
dotnet publish windows/AwesomeInterview.Desktop/AwesomeInterview.Desktop.csproj -c Release -r win-x64 --self-contained true -o windows/publish/AwesomeInterview-1.0-W-win-x64
```

工程版本号 `1.0-W` 独立于 Web 与 Android 版本。

### 当前验收范围

已完成 x64 构建和本机启动烟测。真实账号登录、文件导入/导出、十条提醒送达、系统关机跨时区、完全退出后的计划通知与跨账号清理仍需手动走查；在这些测试完成前不视作正式发行版。
