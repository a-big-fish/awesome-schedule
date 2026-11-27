# Awesome Schedule Android

Android 测试版（`1.0.0-alpha3`）。登录、日历、待安排、待办、面经、面试、简历和账号设置使用原生界面。日程、待安排和待办写入设备加密缓存后立即显示，联网后由应用发送待同步操作。面经导入和写入、简历上传需要在线。

## 本地构建

需要 JDK 17、Android SDK Platform 36、Build Tools 36 和可访问 Google Maven 的网络。

```powershell
$env:JAVA_HOME = 'C:\Program Files\Java\jdk-17.0.13'
$env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
.\gradlew.bat assembleDebug -PserverOrigin=https://your-domain.example
```

将 `your-domain.example` 换成自己的 HTTPS 服务域名，默认值为 `https://example.invalid`。调试 APK 位于 `app/build/outputs/apk/debug/`。首次登录需要服务器运行，断网重启时可通过设备锁屏验证恢复本人缓存与待同步操作。系统提醒仍需真机验收。

顶部应用名称可进入面经广场、我的面试和个人设置。日历提供日、周、月视图，顶部日期可快速跳转。顶栏「待同步修改」可查看待推送及失败操作。面经可在原生界面编辑、导入和导出，PDF 预览可保存当前页 PNG 或整份 PDF。

调试构建可以加 `-PemulatorFixture=true`，以独立应用 ID 连接 Android 模拟器宿主机的 `127.0.0.1:8878`。此模式用于合成数据验收。发布构建要求 HTTPS。

每轮 Android 开发的页面截图保存在仓库根目录的 `app/acceptance/YYYY-MM-DD/`；采集方式与截图范围见 [验收说明](../app/acceptance/README.md)。

## Web 服务

Android 客户端连接自建 Web 服务，沿用同一账号。Web 简历助手可在站点「我的面试」使用，其模型设置和运行说明见 [Web 使用与运维](../app/README.md)。
