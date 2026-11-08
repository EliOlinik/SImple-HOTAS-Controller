namespace HotasController.Core.Presets;

public interface IPresetStore
{
    IReadOnlyList<Preset> Load();

    /// <summary>Adds the preset, replacing any existing one with the same name (case-insensitive).</summary>
    void Save(Preset preset);

    /// <summary>Removes the preset with this name; no-op if absent.</summary>
    void Delete(string name);
}
