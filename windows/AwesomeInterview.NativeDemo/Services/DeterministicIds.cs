using System;
using System.Security.Cryptography;
using System.Text;

namespace AwesomeInterview.NativeDemo.Data;

// The server validates every client-chosen id against uuid5(NAMESPACE_URL, key); Java/JS
// clients had to hand-roll the same SHA-1 variant, so the vectors in NativeChecks pin it.
public static class DeterministicIds
{
    static readonly byte[] NamespaceUrl = Convert.FromHexString("6ba7b8119dad11d180b400c04fd430c8");

    public static string IdeaCreate(string owner, string operationId) => UuidV5($"idea-create:{owner}:{operationId}");
    public static string CalendarCreate(string owner, string operationId) => UuidV5($"calendar:{owner}:{operationId}");
    public static string IdeaEvent(string owner, string ideaId) => UuidV5($"idea:{owner}:{ideaId}");

    public static string UuidV5(string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var buffer = new byte[NamespaceUrl.Length + nameBytes.Length];
        NamespaceUrl.CopyTo(buffer, 0);
        nameBytes.CopyTo(buffer, NamespaceUrl.Length);
        var hash = SHA1.HashData(buffer);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);  // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);  // RFC 4122 variant
        var hex = Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
    }
}
