# 管理后台渠道分类验收

本轮按 Web 网页、Windows 应用、Android App、历史未分类展示请求样本、错误数及 P95。下方耗时、阶段、趋势和慢请求随渠道筛选，卡片保留同时间段的全渠道对比。

- `01-all-channels.png`：全渠道汇总及分组表。
- `02-android-filter.png`：选中 Android 后，表格与耗时统计只显示 Android 请求。

截图为 Chrome 对实际 FastAPI/Jinja/JS 路由的运行渲染。脚本使用隔离的临时 Memory Mapper、SQLite 和合成管理员，不连接生产数据。Playwright 通过 TestClient 转发同源请求，非手写 HTML 原型。`scripts/capture_admin_channels.py` 可复现。

客户端用固定请求头或专属 User-Agent 后缀声明渠道；不保存完整 User-Agent、账号或 IP。此标识仅供统计，不能用于权限判断。旧样本通过 SQLite 增列自动保留在未分类；旧版客户端需更新后才能准确声明渠道。分类数字不是注册人数或 DAU。
