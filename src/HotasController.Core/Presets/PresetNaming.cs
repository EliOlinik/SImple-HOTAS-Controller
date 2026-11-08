namespace HotasController.Core.Presets;

/// <summary>Rules for preset names, including the built-in placeholder that is never saved.</summary>
public static class PresetNaming
{
    /// <summary>Label of the built-in, read-only dropdown item that shows the live state of the controllers.</summary>
    public const string DefaultLabel = "<Default>";

    /// <summary>True when the name is the placeholder's label (case-insensitive, ignoring surrounding spaces).</summary>
    public static bool IsDefaultLabel(string? name) =>
        name is not null && string.Equals(name.Trim(), DefaultLabel, StringComparison.OrdinalIgnoreCase);
}
