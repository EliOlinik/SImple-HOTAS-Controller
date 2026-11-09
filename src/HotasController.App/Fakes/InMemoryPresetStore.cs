using HotasController.Core.Presets;

namespace HotasController.App.Fakes;

/// <summary>Keeps presets in memory only, so <c>--fake</c> runs never write to disk.</summary>
public sealed class InMemoryPresetStore : IPresetStore
{
    private readonly object _gate = new();
    private readonly List<Preset> _presets = new();

    public string? LoadProblem => null;

    public IReadOnlyList<Preset> Load()
    {
        lock (_gate) return _presets.ToList();
    }

    public void Save(Preset preset)
    {
        lock (_gate)
        {
            _presets.RemoveAll(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
            _presets.Add(preset);
        }
    }

    public void Delete(string name)
    {
        lock (_gate)
            _presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
