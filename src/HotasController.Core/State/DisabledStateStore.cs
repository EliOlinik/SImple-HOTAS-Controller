using System.Text.Json;

namespace HotasController.Core.State;

/// <summary>JSON-file backed <see cref="IDisabledStateStore"/>. Thread-safe within a process.</summary>
public sealed class DisabledStateStore : IDisabledStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;

    public DisabledStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "HotasController", "disabled-devices.json");

    public IReadOnlyList<DisabledRecord> Load()
    {
        lock (_gate) return ReadUnlocked();
    }

    public void Add(DisabledRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            var list = ReadUnlocked().ToList();
            list.RemoveAll(r => Same(r.PhysicalDeviceInstanceId, record.PhysicalDeviceInstanceId));
            list.Add(record);
            WriteUnlocked(list);
        }
    }

    public void Remove(string physicalDeviceInstanceId)
    {
        lock (_gate)
        {
            var list = ReadUnlocked().ToList();
            if (list.RemoveAll(r => Same(r.PhysicalDeviceInstanceId, physicalDeviceInstanceId)) > 0)
                WriteUnlocked(list);
        }
    }

    public void Clear()
    {
        lock (_gate) WriteUnlocked(new List<DisabledRecord>());
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private List<DisabledRecord> ReadUnlocked()
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
            var items = JsonSerializer.Deserialize<List<DisabledRecord>>(json, JsonOptions);
            if (items is null || items.Any(i => i is null || i.PhysicalDeviceInstanceId is null))
                throw new JsonException("invalid content");
            return items;
        }
        catch (JsonException)
        {
            try { File.Copy(_path, _path + ".corrupt", overwrite: true); } catch { /* best effort */ }
            return new();
        }
    }

    private void WriteUnlocked(List<DisabledRecord> items)
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
