# Windows 发布流程

线上入口是 [下载 Windows 版](https://schedule.hunaochufang.xyz/download/windows)。站点从 `WINDOWS_RELEASE_DIR` 读取文件，默认为 `data/windows-release`。Windows 项目与 Web 放在同一个仓库中，Windows 版本号、`win` 更新渠道和安装产物独立管理。

## Preview 30 迁移

当前可下载的 Preview 30 包含 `Install.cmd`。首个签名 EXE 上线后，Preview 30 用户需要手动下载安装一次。先从托盘退出旧版，再用同一个 Windows 账户运行安装程序。首次打开新版时，应用会读取已有的账号缓存和设置。

Velopack 的应用 ID 是 `AwesomeInterviewWindows`。安装文件与用户数据使用不同目录，保留现有两处数据位置。

| 内容 | 本机位置 |
| --- | --- |
| 账号缓存、面经草稿、待同步队列 | `%LOCALAPPDATA%\AwesomeInterviewNative\accounts` |
| 桌面设置和随身日程外观 | `%LOCALAPPDATA%\AwesomeInterviewNativeDemo` |
| Preview 30 的程序 | `%LOCALAPPDATA%\Programs\AwesomeInterviewNativeDemo` |
| Velopack 安装的程序 | `%LOCALAPPDATA%\AwesomeInterviewWindows` |

## 构建签名版本

准备 .NET 10 SDK、Velopack CLI 1.2.161 和 Windows SDK 的 SignTool。Windows 当前用户证书存储 `Cert:\CurrentUser\My` 需要具备可用的 RSA 代码签名证书及私钥。首次打包时写入公钥，后续版本使用同一签名密钥，以便已安装客户端验证更新清单。换用新密钥前，应安排新版安装程序的手动迁移。

从仓库根目录在 PowerShell 7 执行。每个版本使用新编号和新暂存目录。将 `$signerThumbprint` 替换为本机签名证书的指纹。

```powershell
dotnet tool install --global vpk --version 1.2.161
./windows/scripts/Package-Velopack.ps1 -Version '1.6.0-preview.31' -ReleaseNotes './release-notes.md' -OutputDir './windows/artifacts/velopack-31' -CertificateThumbprint $signerThumbprint
```

发布后续版本时传入 `-PreviousReleaseDir <上一版的 release 目录>`。该目录包含已有的 `.nupkg`，Velopack 会用它生成差异包。打包脚本检查安装程序的 Authenticode 签名，并为 `releases.win.json` 生成 RSA/SHA-256 签名。程序内的更新源默认是本站 `/download/windows/updates/`。其他部署可通过 `-FeedUrl` 在构建时指定自己的 HTTPS 更新目录。

## 发布与验证

1. 从暂存目录的 `release` 中上传版本化的 EXE 和 `.nupkg` 到 `WINDOWS_RELEASE_DIR`，保留仍在更新清单中的旧包。
2. 上传 `releases.win.json` 及其 `.sig`。完成后替换 `release.json`，网页随即指向新版 EXE。更新文件的输出目录与网站的服务目录应分开。
3. 匿名打开 `/download/windows` 检查版本、下载和 SHA-256。使用一个已有版本安装测试下一版，核对更新说明、差异包、完整包回退和重启后的草稿、待同步记录、随身日程外观。

仅本地验包时可使用 `-UnsignedTest` 替代证书参数。该模式会生成 EXE 和 Velopack 包，应用内更新保持禁用，发布目录也没有签名文件或网页安装包清单。

[Velopack 分发文档](https://docs.velopack.io/distributing/overview) · [签名文档](https://docs.velopack.io/packaging/signing) · [差异包](https://docs.velopack.io/packaging/deltas)
