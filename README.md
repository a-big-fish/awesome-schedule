# Awesome Interview

**开源面试准备工具，支持求职日程、面经复盘和 AI 简历助手。**

[在线体验](https://schedule.hunaochufang.xyz/) · [Android 客户端](android/README.md) · [下载 Windows 版](https://schedule.hunaochufang.xyz/download/windows) · [MIT License](LICENSE)

Awesome Interview is a self-hosted workspace for interview scheduling, interview notes, and AI-assisted resume review. It uses FastAPI and Jinja2 and includes Android and Windows clients.

## 在线体验

访问 **[schedule.hunaochufang.xyz](https://schedule.hunaochufang.xyz/)**。面经广场支持游客浏览公开记录，登录后可管理自己的日程、简历和面经。注册状态以站点提示为准。

普通日程、面经记录和导出可直接使用。AI 简历助手需要可用的模型配置。

Web 服务同时包含个人工作台与企业会议日程。Android 为 alpha 测试版，Windows 提供原生预览和旧版 WebView2 客户端。两端共用服务端账号和数据，设备验收范围见各自说明。

## 功能

- **个人日程**支持日、周、月视图、会议邀请解析及冲突检查。Web 日历可先读取本地缓存，支持本地优先写入并在后台同步。
- **面经管理**支持逐题录入、全文解析、Markdown 或文字层 PDF 导入，记录多级追问与复盘，并导出 Markdown 或 PDF。
- **面经广场**展示公开面经，可按公司筛选。
- **Web 简历助手**在「我的面试」中提供 PDF 预览、基于简历的专项练习，以及可定位原文的改写建议。简历优化可选最多两个模型，对照逐条流式结果并回看分析历史。详见 [Web 使用与运维说明](app/README.md)。
- **企业会议日程**集中解析和安排会议与面试，可设置工作与休息时段。本地模式提供企业入口。

## 仓库结构

| 目录 | 内容 |
| --- | --- |
| `app/` | Web 服务、页面、API、业务逻辑和静态资源。参见 [Web 说明](app/README.md) |
| `interviewschedule/` | 跨模块的时间处理工具 |
| `android/` | Android 应用，详见 [Android 说明](android/README.md) |
| `windows/` | Windows 桌面应用，详见 [Windows 说明](windows/README.md) |
| `tests/` | 自动化测试 |
| `scripts/` | 数据迁移及管理员维护脚本 |
| `mockups/` | 早期界面原型与合成示例简历 |

这是一个单仓库：`app/` 已是 Web 服务目录，`android/` 和 `windows/` 是客户端工程。三者共享服务端数据契约；目前没有独立的 `web/` 或通用 `app/` 客户端目录。

## 本地运行 Web

需要 Python 3.10+ 和 [uv](https://docs.astral.sh/uv/)。在仓库根目录运行：

```bash
cp .env.example .env
uv sync
uv run uvicorn app.main:app --reload --host 127.0.0.1 --port 8291
```

Windows PowerShell 可用 `Copy-Item .env.example .env` 代替 `cp`。访问 `http://127.0.0.1:8291/`。示例配置采用 `APP_MODE=local` 和 `STORAGE_BACKEND=memory`，无需数据库或 AI 密钥，重启后内存中的记录会消失。

本地模式包含个人与企业入口。要持久化，请配置 PostgreSQL 连接，并将 `STORAGE_BACKEND` 改为 `postgres`；`rustfs` 和 `minio` 也使用 PostgreSQL 保存元数据，另用对应 S3 兼容存储保存文件。上传的简历和运行数据都不应提交到 Git。

### 多用户个人模式

在 `.env` 中设置 `APP_MODE=public_personal`、持久化存储，以及你自己的 HTTPS `PUBLIC_BASE_URL`。此模式只开放个人应用与账号相关路由。注册默认关闭；如需邀请或开放注册，设置 `REGISTRATION_MODE`，并按需要配置邀请码、邮箱验证和 SMTP。对外部署前应检查 HTTPS、数据库备份、账号与文件访问控制；不要直接把开发服务器暴露到公网。

默认 AI 模型和自定义模型均在服务端调用。配置、数据存储和请求排查见 [Web 使用与运维说明](app/README.md)。

## 构建客户端

客户端需要连接你部署的 HTTPS 服务。源码默认指向不可用的 `https://example.invalid`，不会自动连接本项目作者的服务器。

**Android**：安装 JDK 17、Android SDK Platform 36 和 Build Tools 36，然后在 `android/` 下运行：

```powershell
.\gradlew.bat assembleDebug -PserverOrigin=https://your-domain.example
```

**Windows** 提供 WPF 原生客户端和旧版 WebView2 测试版。[下载 Windows 版](https://schedule.hunaochufang.xyz/download/windows)查看当前发布版本。[Windows 客户端说明](windows/README.md)包含源码构建、更新和 Preview 30 迁移步骤。

## 测试

```bash
uv run pytest -q
```

前端 JavaScript 测试使用 `node --test tests/js/*.test.cjs`。Android 可在 `android/` 执行 `./gradlew lintDebug`。Windows 的构建和原生检查见 [Windows 客户端说明](windows/README.md)。提醒送达、系统权限和离线行为需在对应设备验证。

## 安全与数据

- `.env`、本机数据库、`data/`、构建产物、签名材料和运行日志都应留在本机。提交前运行 `git status`，不要提交真实简历、面经或账号数据。
- `mockups/sample-resume.pdf` 及对应 HTML 是合成样例，仅供界面原型使用。
- 仓库历史可能仍保留早期开发环境的路径或域名；发布完整 Git 历史前应单独审查历史记录。
- `docs/` 存放本地 PRD 与工作文档，由 Git 忽略。公开运行说明维护在仓库与各客户端的 README。

## 许可证

本项目自有代码采用 [MIT License](LICENSE)。`app/static/vendor/` 中的第三方脚本保留各自文件头部的版权与许可证声明。
