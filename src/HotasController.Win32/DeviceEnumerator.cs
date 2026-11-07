using HotasController.Core;
using HotasController.Core.Abstractions;
using HotasController.Core.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;

namespace HotasController.Win32;

/// <summary>Read-only enumeration of present HIDClass devnodes via SetupAPI / CfgMgr32.</summary>
public sealed class DeviceEnumerator : IHidNodeSource
{
    // Setup class GUID of HIDClass devnodes and interface GUID of HID device interfaces.
    internal static readonly Guid HidClassGuid = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");
    private static readonly Guid HidInterfaceGuid = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    private const uint SpdrpDeviceDesc = 0x00;
    private const uint SpdrpFriendlyName = 0x0C;
    private const uint DnHasProblem = 0x00000400;
    private const uint CmProbDisabled = 22;
    private const uint CrSuccess = 0;
    private const int MaxIdLength = 512;

    public unsafe IReadOnlyList<HidNode> EnumerateHidNodes()
    {
        var result = new List<HidNode>();
        var classGuid = HidClassGuid;

        var rawSet = PInvoke.SetupDiGetClassDevs(&classGuid, null, default, SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT);
        using var set = new SetupDiDestroyDeviceInfoListSafeHandle(rawSet.Value, ownsHandle: true);
        if (set.IsInvalid) return result;

        for (uint index = 0; ; index++)
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
            if (!PInvoke.SetupDiEnumDeviceInfo(set, index, ref data)) break;

            var instanceId = GetInstanceId(set, ref data);
            if (instanceId is null) continue;

            var name = GetStringProperty(set, data, SpdrpFriendlyName)
                       ?? GetStringProperty(set, data, SpdrpDeviceDesc)
                       ?? instanceId;

            var hasVidPid = InstanceIdParser.TryGetVidPid(instanceId, out var vid, out var pid);


            var usage = TryReadUsage(instanceId);

            var physicalId = FindPhysicalDevice(data.DevInst);

            result.Add(new HidNode(
                instanceId,
                name,
                physicalId ?? instanceId,
                physicalId is null ? null : GetBusReportedName(physicalId),
                hasVidPid ? vid : null,
                hasVidPid ? pid : null,
                IsDisabled(data.DevInst),
                usage));
        }

        return result;
    }

    internal static unsafe string? GetInstanceId(SetupDiDestroyDeviceInfoListSafeHandle set, ref SP_DEVINFO_DATA data)
    {
        Span<char> buffer = stackalloc char[MaxIdLength];
        fixed (char* p = buffer)
        fixed (SP_DEVINFO_DATA* d = &data)
        {
            uint required;
            return PInvoke.SetupDiGetDeviceInstanceId(new HDEVINFO(set.DangerousGetHandle()), d, new PWSTR(p), (uint)buffer.Length, &required)
                ? new string(p)
                : null;
        }
    }

    private static unsafe string? GetStringProperty(SetupDiDestroyDeviceInfoListSafeHandle set, SP_DEVINFO_DATA data, uint property)
    {
        Span<byte> buffer = stackalloc byte[1024];
        uint required;
        if (!PInvoke.SetupDiGetDeviceRegistryProperty(set, data, (SETUP_DI_REGISTRY_PROPERTY)property, out _, buffer, out required)) return null;
        var text = System.Text.Encoding.Unicode.GetString(buffer[..(int)Math.Min(required, (uint)buffer.Length)]).TrimEnd('\0');
        return text.Length == 0 ? null : text;
    }

    internal static (bool Ok, uint Status, uint Problem) GetStatus(uint devInst)
    {
        var ok = PInvoke.CM_Get_DevNode_Status(out var status, out var problem, devInst, 0) == CrSuccess;
        return (ok, (uint)status, (uint)problem);
    }

    internal static unsafe bool IsDisabled(uint devInst)
    {
        CM_DEVNODE_STATUS_FLAGS status;
        CM_PROB problem;
        return PInvoke.CM_Get_DevNode_Status(out status, out problem, devInst, 0) == CrSuccess
               && ((uint)status & DnHasProblem) != 0
               && (uint)problem == CmProbDisabled;
    }

    /// <summary>
    /// Walks up from a HID node to the USB device it belongs to, skipping composite
    /// interface nodes (<c>...&amp;MI_xx</c>) so all interfaces of one device share a key.
    /// </summary>
    private static unsafe string? FindPhysicalDevice(uint devInst)
    {
        uint current = devInst;
        string? last = null;
        for (var depth = 0; depth < 4; depth++)
        {
            uint parent;
            if (PInvoke.CM_Get_Parent(out parent, current, 0) != CrSuccess) break;

            var id = GetDeviceId(parent);
            if (id is null) break;
            last = id;

            if (!InstanceIdParser.IsCompositeInterface(id)) break;
            current = parent;
        }
        return last;
    }

    private static unsafe string? GetDeviceId(uint devInst)
    {
        Span<char> buffer = stackalloc char[MaxIdLength];
        fixed (char* p = buffer)
        {
            return PInvoke.CM_Get_Device_IDW(devInst, new PWSTR(p), (uint)buffer.Length, 0) == CrSuccess
                ? new string(p)
                : null;
        }
    }

    private static unsafe HidUsage? TryReadUsage(string instanceId)
    {
        var interfaceGuid = HidInterfaceGuid;
        uint size;
        fixed (char* id = instanceId)
        {
            if (PInvoke.CM_Get_Device_Interface_List_Size(out size, interfaceGuid, new PWSTR(id), 0) != CrSuccess || size <= 1)
                return null;

            var buffer = new char[size];
            fixed (char* b = buffer)
            {
                if (PInvoke.CM_Get_Device_Interface_List(interfaceGuid, new PWSTR(id), new PZZWSTR(b), size, 0) != CrSuccess)
                    return null;
                var path = new string(b);
                return path.Length == 0 ? null : HidUsageReader.Read(path);
            }
        }
    }

    // DEVPKEY_Device_BusReportedDeviceDesc: the product string the USB device reports about itself.
    private static readonly DEVPROPKEY BusReportedDeviceDesc = new()
    {
        fmtid = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"),
        pid = 4,
    };

    private static unsafe string? GetBusReportedName(string deviceInstanceId)
    {
        fixed (char* id = deviceInstanceId)
        {
            if (PInvoke.CM_Locate_DevNode(out var devInst, new PWSTR(id), 0) != CrSuccess) return null;

            var key = BusReportedDeviceDesc;
            DEVPROPTYPE type;
            Span<byte> buffer = stackalloc byte[512];
            uint size = (uint)buffer.Length;
            if (PInvoke.CM_Get_DevNode_Property(devInst, key, out type, buffer, ref size, 0) != CrSuccess) return null;
            var text = System.Text.Encoding.Unicode.GetString(buffer[..(int)Math.Min(size, (uint)buffer.Length)]).TrimEnd('\0');
            return text.Length == 0 ? null : text;
        }
    }
}
