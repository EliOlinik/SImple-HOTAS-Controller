using System.Text.Json;
using HotasController.Core.Storage;

namespace HotasController.Core.Presets;

/// <summary>
/// JSON-file backed <see cref="IPresetStore"/>. Thread-safe within a process.
/// A file that is damaged, too large, over the limits or a link is ignored (and reported through <see cref="LoadProblem"/>).
/// </summary>
public sealed class PresetStore : IPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private volatile string? _loadProblem;

    public PresetStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static string DefaultPath { get; } = Path.Combine(DataFolderPolicy.DefaultPath, "presets.json");

    public string? LoadProblem => _loadProblem;

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

            var problem = DataFileValidator.ValidatePresets(list);
            if (problem is not null) throw new InvalidOperationException(problem); // reads on after "Could not save the preset: "

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

        List<Preset?>? items = null;
        try
        {
            items = JsonSerializer.Deserialize<List<Preset?>>(json, JsonOptions);
            problem = DataFileValidator.ValidatePresets(items);
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
        $"Your saved presets ({Path.GetFileName(_path)}) were ignored: {problem}. " +
        "Saving a preset will replace the file." +
        (backedUp ? $" A copy was kept as {Path.GetFileName(_path)}.corrupt." : "");

    private void WriteUnlocked(List<Preset> items) =>
        SafeJsonFile.WriteAtomic(_path, JsonSerializer.Serialize(items, JsonOptions));
}
