using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Exceptions;

namespace AwesomeInterview.NativeDemo;

internal sealed class WindowsUpdater
{
    static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };
    readonly UpdateManager? manager;

    internal static string Version =>
        typeof(WindowsUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+', 2)[0];

    internal string? UnavailableReason { get; }
    internal string DownloadPage { get; } = "https://schedule.hunaochufang.xyz/download/windows";

    internal WindowsUpdater()
    {
        var metadata = typeof(WindowsUpdater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(item => item.Key, item => item.Value);
        string? publicKey = metadata.GetValueOrDefault("WindowsUpdatePublicKey");
        string? feed = metadata.GetValueOrDefault("WindowsUpdateFeed");
        if (Uri.TryCreate(feed, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps)
            DownloadPage = new Uri(address, "../").AbsoluteUri.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(publicKey) || address == null)
        {
            UnavailableReason = "此版本可通过网页手动更新。";
            return;
        }
        try
        {
            var source = new SignedWindowsUpdateSource(Http, address, Convert.FromBase64String(publicKey));
            var installed = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "win" });
            if (installed.IsInstalled && !installed.IsPortable)
                manager = installed;
            else
                UnavailableReason = "请先使用 Windows 安装程序安装，再检查更新。";
        }
        catch (Exception error) when (IsExpected(error))
        {
            UnavailableReason = "更新配置无法读取，请从网页下载安装程序。";
        }
    }

    internal Task<UpdateInfo?> CheckAsync() => manager!.CheckForUpdatesAsync();

    internal Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken token) =>
        manager!.DownloadUpdatesAsync(update, progress, token);

    internal void Apply(UpdateInfo update) =>
        manager!.WaitExitThenApplyUpdates(update.TargetFullRelease, restart: true);

    internal static bool IsExpected(Exception error) =>
        error is ArgumentException or FormatException or IOException or HttpRequestException or
            OperationCanceledException or CryptographicException or UnauthorizedAccessException or
            InvalidOperationException or System.Text.Json.JsonException or ChecksumFailedException or
            AcquireLockFailedException or NotInstalledException or TimeoutException or
            System.ComponentModel.Win32Exception;

    internal static string ErrorMessage(Exception error) => error switch
    {
        CryptographicException or InvalidDataException or ChecksumFailedException => "更新文件校验失败，请重试或使用网页安装包。",
        OperationCanceledException => "更新下载超时，请稍后重试。",
        HttpRequestException => "无法连接 Windows 更新服务，请稍后重试。",
        UpdatePreparationException => error.Message,
        _ => "更新未完成，请重试或使用网页安装包。"
    };
}

internal sealed class UpdatePreparationException(string message) : InvalidOperationException(message);
