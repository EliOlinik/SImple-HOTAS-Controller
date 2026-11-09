using HotasController.Core.Presets;
using HotasController.Core.State;

namespace HotasController.Core.Storage;

/// <summary>
/// Checks data read from disk (or about to be written) against <see cref="DataFileLimits"/>.
/// Each method returns null when the data is acceptable, otherwise a short description of the first problem.
/// A file with any problem is rejected as a whole rather than partly used.
/// </summary>
public static class DataFileValidator
{
    public static string? ValidatePresets(IReadOnlyList<Preset?>? presets)
    {
        if (presets is null) return "it has no content";
        if (presets.Count > DataFileLimits.MaxPresets)
            return $"it holds {presets.Count} presets (the limit is {DataFileLimits.MaxPresets})";

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            if (preset is null) return "it contains an empty preset";
            if (string.IsNullOrWhiteSpace(preset.Name)) return "a preset has no name";
            if (preset.Name.Length > DataFileLimits.MaxPresetNameLength)
                return $"a preset name is longer than {DataFileLimits.MaxPresetNameLength} characters";
            if (HasControlCharacters(preset.Name)) return "a preset name contains control characters";
            if (!names.Add(preset.Name.Trim())) return $"the preset name \"{preset.Name}\" appears twice";

            if (preset.Entries is null) return $"the preset \"{preset.Name}\" has no device list";
            if (preset.Entries.Count > DataFileLimits.MaxDevices)
                return $"the preset \"{preset.Name}\" lists {preset.Entries.Count} devices (the limit is {DataFileLimits.MaxDevices})";

            foreach (var entry in preset.Entries)
            {
                if (entry is null) return $"the preset \"{preset.Name}\" contains an empty device entry";
                var problem = ValidateDevice(entry.PhysicalDeviceInstanceId, entry.Name, entry.VendorId, entry.ProductId);
                if (problem is not null) return $"the preset \"{preset.Name}\": {problem}";
            }
        }
        return null;
    }

    public static string? ValidateDisabledRecords(IReadOnlyList<DisabledRecord?>? records)
    {
        if (records is null) return "it has no content";
        if (records.Count > DataFileLimits.MaxDevices)
            return $"it holds {records.Count} devices (the limit is {DataFileLimits.MaxDevices})";

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (record is null) return "it contains an empty record";
            var problem = ValidateDevice(record.PhysicalDeviceInstanceId, record.Name, record.VendorId, record.ProductId);
            if (problem is not null) return problem;
            if (!ids.Add(record.PhysicalDeviceInstanceId)) return "a device appears twice";
        }
        return null;
    }

    private static string? ValidateDevice(string? instanceId, string? name, int? vendorId, int? productId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return "a device has no id";
        if (instanceId.Length > DataFileLimits.MaxInstanceIdLength)
            return $"a device id is longer than {DataFileLimits.MaxInstanceIdLength} characters";
        if (HasControlCharacters(instanceId)) return "a device id contains control characters";

        if (name is null) return "a device has no name";
        if (name.Length > DataFileLimits.MaxDeviceNameLength)
            return $"a device name is longer than {DataFileLimits.MaxDeviceNameLength} characters";
        if (HasControlCharacters(name)) return "a device name contains control characters";

        if (vendorId is < 0 or > 0xFFFF || productId is < 0 or > 0xFFFF)
            return "a vendor or product id is out of range";
        return null;
    }

    private static bool HasControlCharacters(string text) => text.Any(char.IsControl);
}
