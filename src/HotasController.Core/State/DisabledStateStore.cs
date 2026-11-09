using System.Text.Json;
using HotasController.Core.Storage;

namespace HotasController.Core.State;

/// <summary>
/// JSON-file backed <see cref="IDisabledStateStore"/>. Thread-safe within a process.
/// A file that is damaged, too large, over the limits or a link is ignored (and reported through <see cref="LoadProblem"/>).
/// </summary>
public sealed class DisabledStateStore : IDisabledStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private volatile string? _loadProblem;

    public DisabledStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static string DefaultPath { get; } = Path.Combine(DataFolderPolicy.DefaultPath, "disabled-devices.json");

    public string? LoadProblem => _loadProblem;

    public IReadOnlyList<DisabledRecord> Load()
    {
        lock (_gate) return ReadUnlocked();
    }

    public void Add(DisabledRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.PhysicalDeviceInstanceId) ||
            record.PhysicalDeviceInstanceId.Length > DataFileLimits.MaxInstanceIdLength)
            throw new ArgumentException("The device id is empty or too long.", nameof(record));

        var clean = record with { Name = DataFileLimits.CleanName(record.Name, DataFileLimits.MaxDeviceNameLength) };
        lock (_gate)
        {
            var list = ReadUnlocked().ToList();
            list.RemoveAll(r => Same(r.PhysicalDeviceInstanceId, clean.PhysicalDeviceInstanceId));
            list.Add(clean);

            // Keep what we write loadable: drop the oldest records beyond the limit.
            while (list.Count > DataFileLimits.MaxDevices)
                list.Remove(list.OrderBy(r => r.DisabledAtUtc).First());

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
        var json = SafeJsonFile.TryRead(_path, out var problem);
        if (problem is not null)
        {
            _loadProblem = Describe(problem, backedUp: false);
            return new();
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            _loadProblem = null;
            return new();
        }

        List<DisabledRecord?>? items = null;
        try
        {
            items = JsonSerializer.Deserialize<List<DisabledRecord?>>(json, JsonOptions);
            problem = DataFileValidator.ValidateDisabledRecords(items);
        }
        catch (JsonException)
        {
            problem = "it is not valid JSON of the expected shape";
        }

        if (problem is null)
        {
            _loadProblem = null;
            return items!.Select(i => i!).ToList();
        }

        _loadProblem = Describe(problem, SafeJsonFile.TryBackup(_path));
        return new();
    }

    private string Describe(string problem, bool backedUp) =>
        $"The list of disabled devices ({Path.GetFileName(_path)}) was ignored: {problem}. " +
        "Controllers this app disabled may not be listed; re-enable them in Device Manager if needed." +
        (backedUp ? $" A copy was kept as {Path.GetFileName(_path)}.corrupt." : "");

    private void WriteUnlocked(List<DisabledRecord> items) =>
        SafeJsonFile.WriteAtomic(_path, JsonSerializer.Serialize(items, JsonOptions));
}
