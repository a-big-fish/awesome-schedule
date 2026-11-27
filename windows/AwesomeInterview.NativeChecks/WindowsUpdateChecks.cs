using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using AwesomeInterview.NativeDemo;

internal static class WindowsUpdateChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var signer = RSA.Create(2048);
        var payload = "simulated full package"u8.ToArray();
        const string name = "AwesomeInterviewWindows-1.6.0-preview.32-full.nupkg";
        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Assets = new[] { new
            {
                SignedWindowsUpdateSource.PackageId,
                Version = "1.6.0-preview.32",
                Type = "Full",
                FileName = name,
                SHA256 = Convert.ToHexString(SHA256.HashData(payload)),
                Size = payload.Length,
                NotesMarkdown = "修复日程同步"
            } }
        });
        byte[] signature = signer.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        byte[] responseManifest = manifest;
        byte[] responseBody = payload;
        using var client = new HttpClient(new Routing(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri!.Segments[^1] switch
            {
                "releases.win.json" => responseManifest,
                "releases.win.json.sig" => signature,
                name => responseBody,
                _ => throw new InvalidOperationException("Unexpected update request")
            })
        })));
        var source = new SignedWindowsUpdateSource(client, new("https://updates.example.test/updates/"),
            signer.ExportSubjectPublicKeyInfo());

        var entry = (await source.GetReleaseFeed(null!, SignedWindowsUpdateSource.PackageId, "win")).Assets.Single();
        check(entry.FileName == name && entry.NotesMarkdown == "修复日程同步", "signed Windows feed preserves version and release notes");
        responseManifest = [..manifest, 0x20];
        check(await RejectsAsync(() => source.GetReleaseFeed(null!, SignedWindowsUpdateSource.PackageId, "win")),
            "tampered Windows feed is rejected before package download");

        var target = Path.GetTempFileName();
        try
        {
            int progress = 0;
            await source.DownloadReleaseEntry(null!, entry, target, value => progress = value);
            check(progress == 100 && File.ReadAllBytes(target).SequenceEqual(payload), "full Windows download verifies size and checksum");
            responseBody = (byte[])payload.Clone();
            responseBody[0] ^= 1;
            check(await RejectsAsync(() => source.DownloadReleaseEntry(null!, entry, target, _ => { })),
                "tampered Windows package with an unchanged size is rejected");
        }
        finally { File.Delete(target); }
    }

    static async Task<bool> RejectsAsync(Func<Task> work)
    {
        try { await work(); }
        catch (CryptographicException) { return true; }
        return false;
    }
}
