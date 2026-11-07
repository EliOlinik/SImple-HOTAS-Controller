using HotasController.Core.Models;
using Windows.Win32;
using Windows.Win32.Devices.HumanInterfaceDevice;
using Windows.Win32.Storage.FileSystem;

namespace HotasController.Win32;

/// <summary>Reads the top-level usage of a HID interface without requesting any access to the device.</summary>
internal static class HidUsageReader
{
    private const int HidpStatusSuccess = 0x110000;

    public static unsafe HidUsage? Read(string devicePath)
    {
        // Access 0 is enough for HidD_GetPreparsedData and never interferes with games holding the device.
        using var handle = PInvoke.CreateFile(
            devicePath,
            0,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            0,
            null);
        if (handle.IsInvalid) return null;

        if (!PInvoke.HidD_GetPreparsedData(handle, out var preparsed) || preparsed.IsInvalid) return null;
        using (preparsed)
        {
            var data = new PHIDP_PREPARSED_DATA(preparsed.DangerousGetHandle());
            if (PInvoke.HidP_GetCaps(data, out var caps).Value != HidpStatusSuccess) return null;
            return new HidUsage(caps.UsagePage, caps.Usage);
        }
    }
}
