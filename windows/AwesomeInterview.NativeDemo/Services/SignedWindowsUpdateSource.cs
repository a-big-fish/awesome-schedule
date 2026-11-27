using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace AwesomeInterview.NativeDemo;

internal sealed class SignedWindowsUpdateSource : IUpdateSource
{
    internal const string PackageId = "AwesomeInterviewWindows";
    const string Feed = "releases.win.json";
    static readonly Regex PackageName = new(@"^AwesomeInterviewWindows-[0-9A-Za-z.-]+-(?:full|delta)\.nupkg\z");
    readonly HttpClient http;
    readonly Uri directory;
    readonly byte[] key;

    internal SignedWindowsUpdateSource(HttpClient http, Uri directory, byte[] key)
    {
        if (directory.Scheme != Uri.UriSchemeHttps || directory.UserInfo.Length != 0 ||
            directory.Query.Length != 0 || directory.Fragment.Length != 0 || !directory.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("更新源必须是 HTTPS 目录。", nameof(directory));
        this.http = http;
        this.directory = directory;
        this.key = key;
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
        Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        if (appId != PackageId || channel != "win")
            throw new InvalidDataException("更新源与当前安装渠道不匹配。");
        var manifestTask = ReadSmallFileAsync(Feed, 2 * 1024 * 1024);
        var signatureTask = ReadSmallFileAsync(Feed + ".sig", 1024);
        await Task.WhenAll(manifestTask, signatureTask);
        byte[] manifest = await manifestTask;
        using var signer = RSA.Create();
        signer.ImportSubjectPublicKeyInfo(key, out int consumed);
        if (consumed != key.Length ||
            !signer.VerifyData(manifest, await signatureTask, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("更新清单签名校验失败。");

        var feed = VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(manifest));
        foreach (var asset in feed.Assets) ValidateAsset(asset);
        return feed;
    }

    async Task<byte[]> ReadSmallFileAsync(string filename, int limit)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync(new Uri(directory, filename), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        for (int count; (count = await stream.ReadAsync(buffer, timeout.Token)) > 0;)
        {
            if (result.Length + count > limit) throw new InvalidDataException("更新清单超过大小限制。");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    static void ValidateAsset(VelopackAsset entry)
    {
        if (entry.PackageId != PackageId || entry.FileName is null || !PackageName.IsMatch(entry.FileName) ||
            entry.SHA256 is not { Length: 64 } || !Regex.IsMatch(entry.SHA256, @"\A[0-9a-fA-F]{64}\z") ||
            entry.Size <= 0)
            throw new InvalidDataException("更新清单中的安装包信息无效。");
    }

    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset entry, string localFile,
        Action<int> progress, CancellationToken cancelToken = default)
    {
        ValidateAsset(entry);
        using var response = await http.GetAsync(new Uri(directory, entry.FileName), HttpCompletionOption.ResponseHeadersRead, cancelToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancelToken);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;
        int previousProgress = -1;
        byte[] buffer = new byte[128 * 1024];
        await using var output = File.Create(localFile);
        for (int count; (count = await input.ReadAsync(buffer, cancelToken)) > 0;)
        {
            received += count;
            if (received > entry.Size) throw new InvalidDataException("更新包超出清单标注大小。");
            digest.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancelToken);
            int percent = (int)(received * 100.0 / entry.Size);
            if (percent != previousProgress) progress(previousProgress = percent);
        }
        if (received != entry.Size ||
            !CryptographicOperations.FixedTimeEquals(digest.GetHashAndReset(), Convert.FromHexString(entry.SHA256)))
            throw new CryptographicException("更新包 SHA-256 校验失败。");
    }
}
