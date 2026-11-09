namespace HotasController.Core.Storage;

/// <summary>
/// Hard limits for the app's JSON data files. Anything beyond them is treated as a damaged or tampered file,
/// so a bad file can never make the app allocate unbounded memory or list unbounded rows.
/// </summary>
public static class DataFileLimits
{
    /// <summary>Largest data file the app will read (4 MiB). Far above anything legitimate.</summary>
    public const int MaxFileBytes = 4 * 1024 * 1024;

    /// <summary>Most presets that can be saved.</summary>
    public const int MaxPresets = 100;

    /// <summary>Most devices in one preset, and most records in the disabled-devices list.</summary>
    public const int MaxDevices = 1000;

    public const int MaxPresetNameLength = 60;
    public const int MaxDeviceNameLength = 200;
    public const int MaxInstanceIdLength = 260;

    /// <summary>Replaces control characters with spaces and cuts the text to <paramref name="maxLength"/>. Null becomes empty.</summary>
    public static string CleanName(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text.Select(c => char.IsControl(c) ? ' ' : c).ToArray();
        var cleaned = new string(chars);
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}
