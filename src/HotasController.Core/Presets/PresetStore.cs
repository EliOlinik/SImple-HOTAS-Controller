using System.Text.Json;

namespace HotasController.Core.Presets;

/// <summary>JSON-file backed <see cref="IPresetStore"/>. Thread-safe within a process.</summary>
public sealed class PresetStore : IPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;

    public PresetStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "HotasController", "presets.json");

    public IReadOnlyList<Preset> Load()
    {
        lock (_gate) return ReadUnlocked();
    }

    public void Save(Preset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        lock (_gate)
        {
            var list = ReadUnlocked().ToList();
            list.RemoveAll(p => SameName(p.Name, preset.Name));
            list.Add(preset);
            WriteUnlocked(list);
        }
    }

    public void Delete(string name)
    {
        lock (_gate)
        {
            var list = ReadUnlocked().ToList();
            if (list.RemoveAll(p => SameName(p.Name, name)) > 0)
                WriteUnlocked(list);
        }
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private List<Preset> ReadUnlocked()
    {
        string json;
        try
        {
            if (!File.Exists(_path)) return new();
            json = File.ReadAllText(_path);
        }
        catch (Exception)
        {
            return new();
        }

        if (string.IsNullOrWhiteSpace(json)) return new();

        try
        {
            var items = JsonSerializer.Deserialize<List<Preset>>(json, JsonOptions);
            if (items is null || items.Any(p => p is null || string.IsNullOrWhiteSpace(p.Name) || p.Entries is null ||
                                                p.Entries.Any(e => e is null || e.PhysicalDeviceInstanceId is null)))
                throw new JsonException("invalid content");
            return items;
        }
        catch (JsonException)
        {
            try { File.Copy(_path, _path + ".corrupt", overwrite: true); } catch { /* best effort */ }
            return new();
        }
    }

    private void WriteUnlocked(List<Preset> items)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(items, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }
}
