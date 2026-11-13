using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AwesomeInterview.NativeDemo.Data;

public record SavedConnection(string Server, string UserId, string Cookie);

// Windows CurrentUser encryption protects private notes and the session cookie at rest.
public sealed class AccountCache
{
    readonly string folder;
    static readonly byte[] entropy = Encoding.UTF8.GetBytes("AwesomeInterview.Native.1.3");
    public AccountCache(string? directory = null) => folder = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AwesomeInterviewNative", "accounts");
    string PathFor(string server, string owner) => Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server + "\n" + owner))) + ".bin");
    string ConnectionPath => Path.Combine(folder, "connection.bin");
    public Task SaveConnectionAsync(SavedConnection value) => SaveAsync(ConnectionPath, value);
    public Task<SavedConnection?> ReadConnectionAsync() => ReadAsync<SavedConnection>(ConnectionPath);
    public Task SaveSnapshotAsync(string server, Snapshot value) { value.Validate(value.UserId); return SaveAsync(PathFor(server, value.UserId), value); }
    public async Task<Snapshot?> ReadSnapshotAsync(string server, string owner)
    {
        var value = await ReadAsync<Snapshot>(PathFor(server, owner)); value?.Validate(owner); return value;
    }
    async Task SaveAsync<T>(string path, T value)
    {
        var encrypted = await Task.Run(() => ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json), entropy, DataProtectionScope.CurrentUser));
        Directory.CreateDirectory(folder); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temp, encrypted); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    async Task<T?> ReadAsync<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var bytes = await File.ReadAllBytesAsync(path);
        var plain = await Task.Run(() => ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser));
        return JsonSerializer.Deserialize<T>(plain, Wire.Json);
    }
    public void Forget(string server, string owner)
    {
        var path = PathFor(server, owner); if (File.Exists(path)) File.Delete(path);
        if (File.Exists(ConnectionPath)) File.Delete(ConnectionPath);
    }
    public void ForgetConnection() { if (File.Exists(ConnectionPath)) File.Delete(ConnectionPath); }
}
