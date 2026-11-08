using System.Text.RegularExpressions;

namespace HotasController.Core;

public static partial class InstanceIdParser
{
    [GeneratedRegex(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPidRegex();

    /// <summary>
    /// Bluetooth form, e.g. <c>VID&amp;0002054C_PID&amp;0CE6</c>: the vendor id is 8 hex digits, where the first four
    /// say who assigned it (0001 = USB-IF, 0002 = Bluetooth SIG) and the last four are the vendor id itself.
    /// </summary>
    [GeneratedRegex(@"VID&[0-9A-F]{4}([0-9A-F]{4})_PID&([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex BluetoothVidPidRegex();

    [GeneratedRegex(@"&MI_[0-9A-F]{2}(\\|$)", RegexOptions.IgnoreCase)]
    private static partial Regex CompositeInterfaceRegex();

    /// <summary>Reads the vendor and product id from a USB (<c>VID_x&amp;PID_y</c>) or Bluetooth (<c>VID&amp;0002xxxx_PID&amp;yyyy</c>) instance id.</summary>
    public static bool TryGetVidPid(string? id, out ushort vid, out ushort pid)
    {
        vid = pid = 0;
        if (id is null) return false;
        var m = VidPidRegex().Match(id);
        if (!m.Success) m = BluetoothVidPidRegex().Match(id);
        if (!m.Success) return false;
        vid = Convert.ToUInt16(m.Groups[1].Value, 16);
        pid = Convert.ToUInt16(m.Groups[2].Value, 16);
        return true;
    }

    /// <summary>A Bluetooth service UUID such as <c>{00001124-0000-1000-8000-00805F9B34FB}</c> (classic HID) or <c>{00001812-...}</c> (HID over GATT).</summary>
    [GeneratedRegex(@"\{0000[0-9A-F]{4}-0000-1000-8000-00805F9B34FB\}", RegexOptions.IgnoreCase)]
    private static partial Regex BluetoothServiceUuidRegex();

    /// <summary>True for devices connected over Bluetooth (classic or low energy), judged from the instance id.</summary>
    public static bool IsBluetooth(string? id) =>
        !string.IsNullOrEmpty(id) &&
        (GetBus(id).StartsWith("BTH", StringComparison.OrdinalIgnoreCase) || BluetoothServiceUuidRegex().IsMatch(id));

    /// <summary>True for devices on the USB bus, judged from the instance id (such as <c>USB\VID_x&amp;PID_y\...</c>).</summary>
    public static bool IsUsb(string? id) => string.Equals(GetBus(id), "USB", StringComparison.OrdinalIgnoreCase);

    /// <summary>The bus part of an instance id (the text before the first backslash), such as <c>USB</c> or <c>BTHENUM</c>; empty if there is none.</summary>
    public static string GetBus(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        var i = id.IndexOf('\\');
        return i < 0 ? "" : id[..i];
    }

    /// <summary>True for USB composite-interface nodes such as <c>USB\VID_x&amp;PID_y&amp;MI_00\6&amp;...</c>.</summary>
    public static bool IsCompositeInterface(string id) => CompositeInterfaceRegex().IsMatch(id);
}
