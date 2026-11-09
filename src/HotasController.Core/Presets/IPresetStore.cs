namespace HotasController.Core.Presets;

public interface IPresetStore
{
    IReadOnlyList<Preset> Load();

    /// <summary>
    /// Null when the last load was fine. Otherwise a message for the user saying the file was ignored and why
    /// (damaged, too large, over the limits, a link, unreadable).
    /// </summary>
    string? LoadProblem { get; }

    /// <summary>
    /// Adds the preset, replacing any existing one with the same name (case-insensitive).
    /// Throws <see cref="InvalidOperationException"/> when the result would break the limits (for example more than 100 presets).
    /// </summary>
    void Save(Preset preset);

    /// <summary>Removes the preset with this name; no-op if absent.</summary>
    void Delete(string name);
}
