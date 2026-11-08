namespace HotasController.Core.Presets;

/// <summary>A named set of desired controller states.</summary>
public sealed record Preset(string Name, IReadOnlyList<PresetEntry> Entries);
