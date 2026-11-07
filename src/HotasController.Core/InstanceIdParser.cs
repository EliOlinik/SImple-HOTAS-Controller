using System.Text.RegularExpressions;

namespace HotasController.Core;

public static partial class InstanceIdParser
{
    [GeneratedRegex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPidRegex();

    [GeneratedRegex(@"&MI_[0-9A-F]{2}(\\|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CompositeInterfaceRegex();

    public static bool TryGetVidPid(string? id, out ushort vid, out ushort pid)
    {
        vid = pid = 0;
        if (id is null) return false;
        var m = VidPidRegex().Match(id);
        if (!m.Success) return false;
        vid = Convert.ToUInt16(m.Groups[1].Value, 16);
        pid = Convert.ToUInt16(m.Groups[2].Value, 16);
        return true;
    }

    /// <summary>True for USB composite-interface nodes such as <c>USB\VID_x&amp;PID_y&amp;MI_00\6&amp;...</c>.</summary>
    public static bool IsCompositeInterface(string id) => CompositeInterfaceRegex().IsMatch(id);
}
