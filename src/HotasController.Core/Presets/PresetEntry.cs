namespace HotasController.Core.Presets;

/// <summary>One controller inside a preset: who it is and whether it should be enabled when the preset is applied.</summary>
public sealed record PresetEntry(
    string PhysicalDeviceInstanceId,
    string Name,
    int? VendorId,
    int? ProductId,
    bool Enabled);
