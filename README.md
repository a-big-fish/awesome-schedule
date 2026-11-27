# Awesome Schedule

**一个开源的个人日程工作台。** 把工作、学习和求职安排放进同一张日历，记下还没定时间的想法，按优先级处理今天的事。遇到面试，还可以在这里读简历、记录追问和复盘。

[在线日程表](https://schedule.hunaochufang.xyz/my-schedule) · [面经广场](https://schedule.hunaochufang.xyz/my-schedule/experiences/plaza) · [下载 Windows 版](https://schedule.hunaochufang.xyz/download/windows) · [当前开发分支](https://github.com/a-big-fish/awesome-schedule/tree/codex/android-native-pages)

Awesome Schedule is a self-hosted calendar and task organizer with offline-friendly scheduling, an idea inbox, and tools for interview notes and resume review. It has a Web app and native Android and Windows clients.

本文按在线站点和当前开发分支整理。GitHub 默认分支保留了较早的源码，自建时请使用上述开发分支。游客可先试用示例日程或浏览面经广场列表，登录后进入自己的日程表。

## 日程与待安排

- **安排和查看**　在日、周、月间切换，用左侧小日历跳到指定日期。日视图并排显示重叠事项，周、月视图连续展示跨天任务。支持重复日程、工作时段、跳过周末、完成与恢复。可以手动录入，也可以粘贴会议邀请，核对解析结果和时间冲突后保存。
- **长按拖动优先级**　在日视图长按日程卡片约 0.8 秒，再左右拖动。重叠时，高优先级靠近时间栏并使用更醒目的样式。优先级为 1–20，同分的卡片也可以调整左右顺序。编辑卡片中的滑块同样可以设置优先级。
- **拖动「待安排」浮窗**　还没定时间的事项先用「添加 idea」记下来。浮窗有事项时显示数量和首条预览，空时直接提供录入入口。拖动浮窗可换位置，浏览器会记住摆放位置，从浮窗打开事项后也可以选定时间，把它变成日程。
- **先改本机，后台同步**　Web 把日程和待安排保存在 IndexedDB。已有缓存时可以本地翻日历，常用修改先更新界面，再借助待同步队列提交。断线时已缓存的内容仍可使用，恢复连接后继续同步。

![Web 日视图，左侧快速日历，中间是跨天与分优先级的任务，右侧是近期安排。](assets/readme/web-calendar-day.png)

<details>
<summary>查看待安排浮窗的空状态</summary>

![日历右下角显示可拖动的添加 idea 入口。](assets/readme/web-idea-bubble.png)

</details>

## 面试、面经与简历

- **我的面试**　集中查看面试安排、入会链接和当前 PDF 简历，给一场面试关联对应的面经。
- **面经整理**　可逐题记录、粘贴全文或导入 Markdown、文字层 PDF。保留多级追问、现场回答和复盘答案，支持预览及导出 Markdown 或 PDF。选择公开的记录可在面经广场按公司浏览。
- **Web 简历助手**　「练一练」围绕上传的简历提问，可参考个人面经里的相关题目。「简历优化」在原文旁给改写建议并定位 PDF 页码，支持自定义模型、最多双模型流式对照和查看分析历史。日程和面经的常用功能无需配置模型。 [模型与运维说明](https://github.com/a-big-fish/awesome-schedule/blob/codex/android-native-pages/app/README.md)

## 客户端

| 客户端 | 可用范围 |
| --- | --- |
| Web | FastAPI、Jinja2 和浏览器本地缓存。个人日程为主要入口，自建本地模式也提供企业会议日程。 |
| Android | 开发分支提供测试版源码。1.3 的业务页原生化和加密离线队列已在本地通过模拟器合成数据验证，待合入开发分支并完成真机验收。 [Android 构建说明](https://github.com/a-big-fish/awesome-schedule/blob/codex/android-native-pages/android/README.md) |
| Windows | WPF 原生预览，含桌面托盘和外观可调的随身日程。下载页现提供 Preview 31 的 ZIP，应用内更新已接入 Velopack，正式签名包和迁移验收待完成。 [Windows 使用与发布说明](https://github.com/a-big-fish/awesome-schedule/blob/codex/android-native-pages/windows/README.md) |

## 开发展望

### 已实现

- [x] **PRD 2.0–2.2 · 个人日程与多用户**　日历、会议邀请解析、日程冲突提醒和个人账号隔离。
- [x] **PRD 2.3、2.5 · 面经记录和面经广场**　逐题与全文录入、多级追问、文件导入、预览导出，以及公开面经的公司筛选。
- [x] **PRD 2.6–2.7 · 性能观测和待安排**　服务端请求观测、可拖动的待安排浮窗，以及将 idea 排入日历。
- [x] **PRD 3.0 · Web 本地优先**　日历和待安排用 JSON 和 IndexedDB 渲染，离线修改走幂等队列。各端使用同一组样例验证核心日视图规则。
- [x] **近日日程交互**　左侧快速日历、日视图长按排序和 1–20 级优先级。
- [x] **PRD 3.2 · 简历助手第一阶段**　基于简历的文字练习与逐条优化建议、自定义模型、双模型流式对照和分析历史。
- [x] **Android 1.3 本地验证与 Windows 原生预览**　Android 原生工作台和加密离线队列已完成模拟器验证。Windows 已提供日程与面试工作台、随身日程和网页下载入口。

### 待开发或验收

- [ ] **PRD 3.0 · 同步后续**　根据性能数据评估按资源增量拉取、多设备并发编辑和旧接口下线。
- [ ] **PRD 3.1 · Web 样式迁移**　保持现有视觉，使用 Tailwind 整理样式并验证桌面、平板和手机布局。
- [ ] **PRD 2.4、2.8 · 面经复习与游客数据**　面经记忆卡片和逐题 AI 复习，游客自建日程及登录后的数据合并。
- [ ] **PRD 2.9、3.2 · 模拟面试与复盘**　按企业、岗位和轮次归纳面经里的考察重点及追问，引用这些观察辅助简历优化。补齐目标岗位模拟面试、复盘及评分。
- [ ] **客户端发布**　Android 1.3 代码合入，以及通知、锁屏、重启和 Doze 的真机验收，Windows 首次签名安装、Preview 30 迁移和端到端自动更新验收。

## 自建 Web

服务端需要 Python 3.10+ 和 [uv](https://docs.astral.sh/uv/)。切到当前开发分支后，在仓库根目录运行。

```bash
cp .env.example .env
uv sync
uv run uvicorn app.main:app --reload --host 127.0.0.1 --port 8291
```

PowerShell 使用 `Copy-Item .env.example .env`。打开 [本机日程表](http://127.0.0.1:8291/my-schedule)。示例配置使用 `APP_MODE=local` 与 `STORAGE_BACKEND=memory`，重启会清除内存记录。需要持久化时使用 PostgreSQL，也可以配合 RustFS 或 MinIO 保存简历等文件。

要部署多用户个人站点，设置 `APP_MODE=public_personal`、持久化存储和 HTTPS `PUBLIC_BASE_URL`。通过 `REGISTRATION_MODE` 配置注册方式，按需启用邀请码、邮箱验证与 SMTP。开发服务器仅用于本地调试。

## 开发

`app/` 是 Web 服务、页面和 API，`android/` 与 `windows/` 分别是原生客户端工程。`tests/` 包含 Web、接口和跨端契约测试。

```bash
uv run pytest -q
node --test tests/js/*.test.cjs
```

`.env`、运行数据、日志、签名材料和真实简历留在本机。项目代码采用 [MIT License](LICENSE)，第三方资源保留各自的版权与许可证。
