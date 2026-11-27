using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace AwesomeInterview.NativeDemo.Data;

internal sealed class SavedLogin(string server, string userId, string identifier, string password)
{
    public string Server { get; } = server;
    public string UserId { get; } = userId;
    public string Identifier { get; } = identifier;
    public string Password { get; } = password;
}

internal sealed record RememberedLogin(string Server, string Identifier);

public record SavedConnection(string Server, string UserId, string Cookie);

// Windows CurrentUser encryption protects private notes and the session cookie at rest.
public sealed class AccountCache
{
    readonly string folder;
    static readonly byte[] entropy = Encoding.UTF8.GetBytes("AwesomeInterview.Native.1.3");
    public AccountCache(string? directory = null) => folder = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AwesomeInterviewNative", "accounts");
    string PathFor(string server, string owner) => Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server + "\n" + owner))) + ".bin");
    string OutboxPathFor(string server, string owner) => PathFor(server, owner) + ".outbox.bin";
    string ConnectionPath => Path.Combine(folder, "connection.bin");
    string LoginPath => Path.Combine(folder, "remembered-login.bin");
    internal Task SaveLoginAsync(SavedLogin value) => SaveAsync(LoginPath, value);
    internal Task<SavedLogin?> ReadLoginAsync() => ReadAsync<SavedLogin>(LoginPath);
    internal void ForgetLogin() { if (File.Exists(LoginPath)) File.Delete(LoginPath); }
    public Task SaveConnectionAsync(SavedConnection value) => SaveAsync(ConnectionPath, value);
    public Task<SavedConnection?> ReadConnectionAsync() => ReadAsync<SavedConnection>(ConnectionPath);
    // The pending-write queue is private per server and owner, and encrypted like the snapshot.
    public Task SaveOutboxAsync(string server, string owner, SyncOutbox value) => SaveAsync(OutboxPathFor(server, owner), value);
    public async Task<SyncOutbox?> ReadOutboxAsync(string server, string owner)
    {
        var value = await ReadAsync<SyncOutbox>(OutboxPathFor(server, owner));
        if(value!=null&&(value.SchemaVersion!=1||value.UserId!=owner))throw new InvalidDataException("本机同步队列版本或账号不匹配，已保留原文件。");
        return value;
    }
    public Task SaveSnapshotAsync(string server, Snapshot value) { value.Validate(value.UserId); return SaveAsync(PathFor(server, value.UserId), value); }
    public async Task<Snapshot?> ReadSnapshotAsync(string server, string owner)
    {
        var value = await ReadAsync<Snapshot>(PathFor(server, owner)); value?.Validate(owner); return value;
    }
    public async Task SaveDocumentAsync(string server,string owner,CachedDocument value)
    {
        var path=PathFor(server,owner)+".document.bin";CachedDocument? old=null;
        try{old=await ReadAsync<CachedDocument>(path);}catch(Exception error)when(error is CryptographicException or JsonException or IOException){}
        long size=value.Document.Pages.Sum(page=>(long)page.Length);if(size>40*1024*1024)throw new InvalidDataException("简历预览缓存不能超过40MB。");
        if(old!=null&&old.Document.Version!=value.Document.Version){
            if(old.Document.Pages.Sum(page=>(long)page.Length)+size<=40*1024*1024)await SaveAsync(path+".previous.bin",old);
            else if(File.Exists(path+".previous.bin"))File.Delete(path+".previous.bin");
        }
        await SaveAsync(path,value);
    }
    public Task<CachedDocument?> ReadDocumentAsync(string server,string owner,bool previous=false) => ReadAsync<CachedDocument>(PathFor(server,owner)+".document.bin"+(previous?".previous.bin":""));
    public Task SaveEditorDraftAsync(string server,string owner,ExperienceData value) => SaveAsync(PathFor(server,owner)+".draft."+Guid.Parse(value.Id).ToString("N")+".bin",value);
    public Task<ExperienceData?> ReadEditorDraftAsync(string server,string owner,string id) => ReadAsync<ExperienceData>(PathFor(server,owner)+".draft."+Guid.Parse(id).ToString("N")+".bin");
    public void ForgetEditorDraft(string server,string owner,string id){var path=PathFor(server,owner)+".draft."+Guid.Parse(id).ToString("N")+".bin";if(File.Exists(path))File.Delete(path);}
    public Task SaveReminderLedgerAsync(string server,string owner,System.Collections.Generic.List<string> values)=>SaveAsync(PathFor(server,owner)+".reminders.bin",values);
    public Task<System.Collections.Generic.List<string>?> ReadReminderLedgerAsync(string server,string owner)=>ReadAsync<System.Collections.Generic.List<string>>(PathFor(server,owner)+".reminders.bin");
    async Task SaveAsync<T>(string path, T value)
    {
        var encrypted = await Task.Run(() =>
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(value, Wire.Json);
            try { return ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        });
        Directory.CreateDirectory(folder); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temp, encrypted); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    async Task<T?> ReadAsync<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var bytes = await File.ReadAllBytesAsync(path);
        var plain = await Task.Run(() => ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser));
        try { return JsonSerializer.Deserialize<T>(plain, Wire.Json); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Forget(string server, string owner)
    {
        var path = PathFor(server, owner); if (File.Exists(path)) File.Delete(path);
        var outbox = OutboxPathFor(server, owner); if (File.Exists(outbox)) File.Delete(outbox);
        var document = path+".document.bin"; if(File.Exists(document))File.Delete(document);
        if(File.Exists(document+".previous.bin"))File.Delete(document+".previous.bin");
        if(File.Exists(path+".reminders.bin"))File.Delete(path+".reminders.bin");
        foreach(var draft in Directory.Exists(folder)?Directory.GetFiles(folder,Path.GetFileName(path)+".draft.*.bin"):Array.Empty<string>())File.Delete(draft);
        if (File.Exists(ConnectionPath)) File.Delete(ConnectionPath);
    }
    public void ForgetConnection() { if (File.Exists(ConnectionPath)) File.Delete(ConnectionPath); }
}
