# Windows 原生预览 1.3.0-W

当前版本已移除主流程中的合成示例，使用现有 Web 账号登录真实服务。下方旧版本记录仅保留演进历史。

## 当前版本的安装与服务要求

解压 `windows/publish/AwesomeInterview-NativeDemo-1.3.0-W-win-x64.zip`，关闭旧版后运行 `Install.cmd`；也可以直接运行 `app/AwesomeInterview.NativeDemo.exe`。无需管理员权限或预装 .NET。当前仍为未签名的用户级脚本安装包。

默认服务地址为 `https://schedule.hunaochufang.xyz/`。密码只在内存中用于登录，不存储、不写日志。会话Cookie和账号快照用Windows当前用户DPAPI加密，保存在 `%LOCALAPPDATA%/AwesomeInterviewNative/accounts/`，按服务地址及账号隔离。旧版演示JSON不会被导入或上传。退出账号清理该账号的快照和登录记录。

**服务端需更新本次代码**，提供 `/api/mobile/v1/profile`、`/plaza`、`/plaza/{id}` 和扩展日程字段。旧部署的原有快照仍可读取，但广场和昵称设置可能提示接口尚未支持。客户端不会代替你部署或重启公网服务。

## 已接入的原生界面

- Cookie/CSRF登录、账号缓存优先、后台同步、离线只读。18h以上缓存标记过期，仍保留供断网查阅；不以旧数据假装新数据。
- 今日任务，日/周/月日历，回到今天，跨天任务，隐藏已完成后释放列位。日期和视图切换不请求数据库，因为内存快照已含所有日期。
- 详情卡片、点击入会、参考链接；新增/编辑/删除、会议邀请解析、重叠确认、完成/恢复。重复任务支持按日展示及完成恢复，整组编辑暂不开放。
- 我的面经层级阅读和回答隐藏；服务端PDF/Markdown导出到系统保存对话框。
- 广场公司筛选、作者、日期排序、滚动加载；昵称修改、只读邮箱；托盘与随身日程共用真实日程状态。

## 校验和仍待完成的部分

运行 `dotnet run --project windows/AwesomeInterview.NativeChecks -c Release` 可检查协议、加密缓存、账号隔离、树形编号、时区与错误分支。真实服务只读联调可通过进程环境变量 `AWESOME_TEST_SERVER` 和 `AWESOME_TEST_COOKIE` 显式提供当前账号会话；不在脚本或仓库中写入它们，检查工具只输出条目数量，不输出响应正文。

本轮C#16项通过，Python完整回归203通过、1跳过，Release自包含发布成功。真实账号操作尚待应用内登录；自动化工具窗口绑定失败，尚未完成视觉/交互、多DPI、干净机器安装验收，不能把编译通过视为体验已验收。

完整PRD仍在实施：我的面试与原生简历渲染，面经原生编辑/全文/文件导入，独立待办编辑，逾期延期废除，默认工作时段联动与原生日期时间选择器，导出分页截图，系统提醒，随身日程透明/颜色/鼠标穿透，以及签名安装器。当前版本不表示这些功能已完成。

---

## 旧版本记录

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
