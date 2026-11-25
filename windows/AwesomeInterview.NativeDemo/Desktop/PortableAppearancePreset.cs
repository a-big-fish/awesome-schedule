using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AwesomeInterview.NativeDemo;

internal sealed record PortableAppearancePreset(string Name, int OverallTransparency, int BackgroundTransparency,
    int BorderTransparency, int TextTransparency, PortableStyle? Style = null, int Version = 1)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FileName { get; init; }

    internal const string OutlineFile = "clear-outline.json";
    internal const string DefaultFile = "soft-silhouette.json";
    internal const string GlassFile = "frosted-glass.json";
    static readonly string[] Packaged = [OutlineFile, DefaultFile, GlassFile];
    static readonly PortableStyle PreviousBundledStyle = new(GlassStrength: 42);
    static readonly PortableStyle PreviousFrostedStyle = new(GlassStrength: 82, GlassRadius: 14, GlassFeather: 16);
    static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    internal static IReadOnlyList<PortableAppearancePreset> BuiltIn => ReadBuiltIn(AppContext.BaseDirectory);

    internal static IReadOnlyList<PortableAppearancePreset> ReadBuiltIn(string appDirectory)
    {
        var folder = Path.Combine(appDirectory, "PortableStyles");
        return Packaged.Select(file => TryRead(Path.Combine(folder, file))).OfType<PortableAppearancePreset>().ToArray();
    }

    internal static string EnsureFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in Packaged.Append("README.md"))
        {
            var destination = Path.Combine(folder, file);
            bool exists = File.Exists(destination);
            bool previous = exists && (file == "README.md"
                ? !File.ReadAllText(destination).Contains("`Style.GlassTone`", StringComparison.Ordinal)
                : MatchesPreviousBundledPreset(TryRead(destination)));
            if (!exists || previous)
            {
                var source = Path.Combine(AppContext.BaseDirectory, "PortableStyles", file);
                if (File.Exists(source)) File.Copy(source, destination, overwrite: previous);
            }
        }
        return folder;
    }

    static bool MatchesPreviousBundledPreset(PortableAppearancePreset? preset) => preset is { BackgroundTransparency: 100 }
        && (preset.Style == PreviousBundledStyle && preset is
                ({ FileName: OutlineFile, Name: "清透描边", OverallTransparency: 0, BorderTransparency: 0, TextTransparency: 0 }
                or { FileName: DefaultFile, Name: "清透淡影", OverallTransparency: 50, BorderTransparency: 100, TextTransparency: 30 })
            || preset.Style == PreviousFrostedStyle && preset is
                { FileName: GlassFile, Name: "磨砂玻璃", OverallTransparency: 0, BorderTransparency: 100, TextTransparency: 0 });

    internal static DesktopPreferences UpgradeSavedStyle(DesktopPreferences saved)
    {
        if (saved.PortableStyle == PreviousBundledStyle && saved.PortableStyleFile is null or OutlineFile or DefaultFile)
            return saved with { PortableStyle = PortableStyle.Default };
        if (saved.PortableStyleFile == GlassFile && saved.PortableStyle == PreviousFrostedStyle &&
            TryRead(Path.Combine(AppContext.BaseDirectory, "PortableStyles", GlassFile))?.Style is { } updated)
            return saved with { PortableStyle = updated };
        return saved;
    }

    internal static IReadOnlyList<PortableAppearancePreset> ReadFolder(string folder)
    {
        var presets = new List<PortableAppearancePreset>();
        foreach (var path in Directory.EnumerateFiles(folder, "*.json")
            .OrderBy(path => Path.GetFileName(path) switch { OutlineFile => 0, DefaultFile => 1, GlassFile => 2, _ => 3 })
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            if (TryRead(path) is { } preset) presets.Add(preset);
        }
        return presets;
    }

    internal static PortableAppearancePreset Import(string path, string folder)
    {
        var preset = Read(path);
        string name = Path.GetFileName(path);
        string destination = Path.Combine(folder, name);
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            return preset;
        for (int index = 2; File.Exists(destination); index++)
            destination = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}-{index}.json");

        string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                JsonSerializer.Serialize(stream, preset, new JsonSerializerOptions { WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            File.Move(temporary, destination);
        }
        finally
        {
            File.Delete(temporary);
        }
        return preset with { FileName = Path.GetFileName(destination) };
    }

    internal static PortableAppearancePreset? TryRead(string path)
    {
        try { return Read(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    static PortableAppearancePreset Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 64 * 1024) throw new JsonException("样式文件过大。");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(nameof(Name), out _) ||
            !root.TryGetProperty(nameof(OverallTransparency), out _) || !root.TryGetProperty(nameof(BackgroundTransparency), out _) ||
            !root.TryGetProperty(nameof(BorderTransparency), out _) || !root.TryGetProperty(nameof(TextTransparency), out _))
            throw new JsonException("样式缺少名称或透明度字段。");

        var preset = root.Deserialize<PortableAppearancePreset>(Options);
        if (preset is not { Version: 1, Name.Length: > 0 and <= 40, OverallTransparency: >= 0 and <= 90,
            BackgroundTransparency: >= 0 and <= 100, BorderTransparency: >= 0 and <= 100, TextTransparency: >= 0 and <= 90 } ||
            string.IsNullOrWhiteSpace(preset.Name) || preset.Style?.IsValid == false)
            throw new JsonException("样式版本、透明度、色值或磨砂参数超出范围。");
        return preset with { FileName = Path.GetFileName(path) };
    }

    internal DesktopPreferences Apply(DesktopPreferences current) => current with
    {
        PortableOverallOpacity = 1 - OverallTransparency / 100d,
        PortableOpacity = 1 - BackgroundTransparency / 100d,
        PortableBorderOpacity = 1 - BorderTransparency / 100d,
        PortableTextOpacity = 1 - TextTransparency / 100d,
        PortableStyle = Style ?? PortableStyle.Default,
        PortableStyleFile = FileName
    };
}
