using System.Diagnostics;
using System.Text.Json.Nodes;
using HotasController.Core;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;
using Windows.Win32.Foundation;

namespace HotasController.Win32;

/// <summary>
/// Read-only capture of everything that could explain why one driver stack can be stopped live and another
/// cannot: the devnode tree under each game controller (status, service, filters, INF, provider, dates),
/// the registry entries of the services involved, matching processes, Restart Manager users of the device
/// interfaces, and the raw output of a few Windows tools. It never opens a device and never changes state.
/// The result is meant for the Dump tool; it contains real instance ids and must not be committed.
/// </summary>
public static class DeviceSnapshot
{
    private const uint CrSuccess = 0;
    private const uint CrBufferSmall = 0x1A;
    private const int MaxIdLength = 512;

    private static readonly Guid DevicePkey = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    private static readonly Guid DriverPkey = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6");
    private static readonly Guid InstallPkey = new("83da6326-97a6-4088-9453-a1923f573b29");
    private static readonly Guid ProblemPkey = new("4340a6c5-93fa-4706-972c-7b648008a5a7");
    private static readonly Guid ContainerPkey = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c");
    private static readonly Guid HidInterface = new("4d1e55b2-f16f-11cf-88cb-001111000030");
    private static readonly Guid UsbDeviceInterface = new("a5dcbf10-6530-11d2-901f-00c04fb951ed");

    // Names follow devpkey.h. Properties a node does not have are simply left out.
    private static readonly (string Name, Guid Fmt, uint Pid)[] Properties =
    [
        ("deviceDesc", DevicePkey, 2), ("hardwareIds", DevicePkey, 3), ("compatibleIds", DevicePkey, 4),
        ("service", DevicePkey, 6), ("class", DevicePkey, 9), ("classGuid", DevicePkey, 10),
        ("driverKey", DevicePkey, 11), ("configFlags", DevicePkey, 12), ("manufacturer", DevicePkey, 13),
        ("friendlyName", DevicePkey, 14), ("locationInfo", DevicePkey, 15), ("pdoName", DevicePkey, 16),
        ("capabilities", DevicePkey, 17), ("upperFilters", DevicePkey, 19), ("lowerFilters", DevicePkey, 20),
        ("busTypeGuid", DevicePkey, 21), ("enumerator", DevicePkey, 24), ("characteristics", DevicePkey, 29),
        ("address", DevicePkey, 30), ("removalPolicy", DevicePkey, 33), ("removalPolicyDefault", DevicePkey, 34),
        ("removalPolicyOverride", DevicePkey, 35), ("installState", DevicePkey, 36), ("locationPaths", DevicePkey, 37),
        ("containerId", ContainerPkey, 2),
        ("driverDate", DriverPkey, 2), ("driverVersion", DriverPkey, 3), ("driverDesc", DriverPkey, 4),
        ("driverInfPath", DriverPkey, 5), ("driverInfSection", DriverPkey, 6), ("matchingDeviceId", DriverPkey, 8),
        ("driverProvider", DriverPkey, 9), ("driverRank", DriverPkey, 14),
        ("installDate", InstallPkey, 100), ("firstInstallDate", InstallPkey, 101),
        ("lastArrivalDate", InstallPkey, 102), ("lastRemovalDate", InstallPkey, 103),
        ("problemCode", ProblemPkey, 3), ("problemStatus", ProblemPkey, 12),
    ];

    private static readonly string[] Keywords = ["saitek", "logitech", "x56", "x52", "x55", "saik", "madcatz", "mad catz", "hotas"];

    public static JsonObject Capture(string label, IEnumerable<string>? extraVendorIds = null)
    {
        var root = new JsonObject
        {
            ["label"] = label,
            ["capturedAt"] = DateTimeOffset.Now.ToString("O"),
            ["tool"] = "HotasController.Dump --snapshot",
            ["elevated"] = IsElevated(),
            ["os"] = Environment.OSVersion.VersionString,
        };

        var hidNodes = new DeviceEnumerator().EnumerateHidNodes();
        var companionSource = new DeviceEnumerator();
        var controllers = GameControllerGrouper.Group(hidNodes, new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            .Select(c => c with { CompanionNodes = companionSource.GetCompanionNodes(c.PhysicalDeviceInstanceId, c.Nodes.Select(n => n.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase)) })
            .ToList();

        var hid = new JsonObject();
        foreach (var n in hidNodes.OrderBy(n => n.InstanceId, StringComparer.OrdinalIgnoreCase))
            hid[n.InstanceId] = new JsonObject
            {
                ["name"] = n.Name,
                ["physicalDevice"] = n.PhysicalDeviceInstanceId,
                ["disabled"] = n.IsDisabled,
                ["usage"] = n.Usage is { } u ? $"{u.Page:X4}:{u.Usage:X4}" : null,
            };
        root["hidNodes"] = hid;

        var ctl = new JsonObject();
        foreach (var c in controllers)
            ctl[c.PhysicalDeviceInstanceId] = new JsonObject
            {
                ["name"] = c.Name,
                ["enabled"] = c.IsEnabled,
                ["toggleTargets"] = new JsonArray(ToggleTargets.Select(c).Select(t => (JsonNode?)t.InstanceId).ToArray()),
                ["companions"] = new JsonArray(c.CompanionNodes.Select(t => (JsonNode?)t.InstanceId).ToArray()),
            };
        root["controllers"] = ctl;

        // Every devnode in the subtree of each controller's physical device, plus the chain of parents above it.
        var tree = new JsonObject();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in controllers)
        {
            if (Locate(c.PhysicalDeviceInstanceId) is not { } dev) continue;
            AddAncestors(dev, tree, visited);
            AddSubtree(dev, tree, visited, parentId: GetParentId(dev), depth: 0);
        }
        // A controller that is disabled is no longer a game controller, so it would vanish from the snapshot exactly
        // when it matters. Also capture every present USB device whose vendor id belongs to a controller (or was
        // named with --vid), so a stuck or disabled device stays visible.
        var vendorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in controllers) if (c.VendorId is { } v) vendorIds.Add($"{v:X4}");
        // Disabled nodes are the ones that drop out of the controller list, so their vendors count too.
        foreach (var n in hidNodes) if (n.IsDisabled && n.VendorId is { } dv) vendorIds.Add($"{dv:X4}");
        foreach (var v in extraVendorIds ?? []) vendorIds.Add(v.Trim().ToUpperInvariant().Replace("VID_", ""));
        root["vendorIds"] = ToArray(vendorIds.OrderBy(v => v, StringComparer.Ordinal));
        foreach (var id in EnumerateUsbDeviceIds())
        {
            if (!vendorIds.Any(v => id.StartsWith($@"USB\VID_{v}&", StringComparison.OrdinalIgnoreCase))) continue;
            if (id.Contains("&MI_", StringComparison.OrdinalIgnoreCase) || visited.Contains(id)) continue;
            if (Locate(id) is not { } dev) continue;
            AddAncestors(dev, tree, visited);
            AddSubtree(dev, tree, visited, parentId: GetParentId(dev), depth: 0);
        }
        root["devnodes"] = tree;

        root["restartManager"] = CaptureRestartManager(tree);
        root["services"] = CaptureServices(tree);
        root["processes"] = CaptureProcesses();
        root["commands"] = CaptureCommands();
        return root;
    }

    // ---------------------------------------------------------------- devnode tree

    private static void AddAncestors(uint dev, JsonObject tree, HashSet<string> visited)
    {
        var current = dev;
        for (var i = 0; i < 8; i++)
        {
            if (PInvoke.CM_Get_Parent(out var parent, current, 0) != CrSuccess) break;
            var id = GetId(parent);
            if (id is null || !visited.Add(id)) break;
            tree[id] = DescribeNode(parent, id, GetParentId(parent), children: null, role: "ancestor");
            current = parent;
        }
    }

    private static void AddSubtree(uint dev, JsonObject tree, HashSet<string> visited, string? parentId, int depth)
    {
        var id = GetId(dev);
        if (id is null || depth > 8) return;

        var childIds = new List<string>();
        if (PInvoke.CM_Get_Child(out var child, dev, 0) == CrSuccess)
        {
            for (var guard = 0; guard < 64; guard++)
            {
                if (GetId(child) is { } cid) childIds.Add(cid);
                if (PInvoke.CM_Get_Sibling(out var sibling, child, 0) != CrSuccess) break;
                child = sibling;
            }
        }

        if (visited.Add(id)) tree[id] = DescribeNode(dev, id, parentId, childIds, role: depth == 0 ? "physical" : "descendant");
        else if (tree[id] is JsonObject existing) existing["children"] = ToArray(childIds);

        foreach (var cid in childIds)
            if (Locate(cid) is { } cdev) AddSubtree(cdev, tree, visited, id, depth + 1);
    }

    private static JsonObject DescribeNode(uint dev, string id, string? parentId, List<string>? children, string role)
    {
        var (ok, status, problem) = DeviceEnumerator.GetStatus(dev);
        var node = new JsonObject
        {
            ["role"] = role,
            ["parent"] = parentId,
            ["children"] = children is null ? null : ToArray(children),
            ["status"] = new JsonObject
            {
                ["ok"] = ok,
                ["raw"] = $"0x{status:X}",
                ["flags"] = ToArray(DescribeStatus(status)),
                ["problemCode"] = problem,
            },
            ["interfaces"] = new JsonObject
            {
                ["hid"] = ToArray(GetInterfaces(id, HidInterface)),
                ["usbDevice"] = ToArray(GetInterfaces(id, UsbDeviceInterface)),
            },
        };

        var props = new JsonObject();
        foreach (var (name, fmt, pid) in Properties)
            if (ReadProperty(dev, fmt, pid) is { } value) props[name] = value;
        node["properties"] = props;
        return node;
    }

    private static IEnumerable<string> DescribeStatus(uint status)
    {
        (uint Bit, string Name)[] flags =
        [
            (0x2, "DN_DRIVER_LOADED"), (0x8, "DN_STARTED"), (0x10, "DN_MANUAL"), (0x20, "DN_NEED_TO_ENUM"),
            (0x100, "DN_NEED_RESTART"), (0x200, "DN_REBAL_CANDIDATE"), (0x400, "DN_HAS_PROBLEM"),
            (0x800, "DN_FILTERED"), (0x1000, "DN_LEGACY_DRIVER"), (0x2000, "DN_DISABLEABLE"),
            (0x4000, "DN_REMOVABLE"), (0x8000, "DN_PRIVATE_PROBLEM"), (0x10000, "DN_MF_PARENT"),
            (0x20000, "DN_MF_CHILD"), (0x40000, "DN_WILL_BE_REMOVED"), (0x80000, "DN_NOT_FIRST_TIME"),
            (0x100000, "DN_STOP_FREE_RES"), (0x200000, "DN_REBAL_CAND_ALL"), (0x400000, "DN_BUSDRIVER"),
            (0x800000, "DN_NO_SHOW_IN_DM"), (0x2000000, "DN_DEVICE_DISCONNECTED"),
        ];
        return flags.Where(f => (status & f.Bit) != 0).Select(f => f.Name);
    }

    /// <summary>Instance ids of all present devnodes under the USB enumerator (any class, enabled or not).</summary>
    private static unsafe List<string> EnumerateUsbDeviceIds()
    {
        var ids = new List<string>();
        fixed (char* usb = "USB")
        {
            var raw = PInvoke.SetupDiGetClassDevs(null, new PCWSTR(usb), default,
                SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_ALLCLASSES | SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT);
            using var set = new SetupDiDestroyDeviceInfoListSafeHandle(raw.Value, ownsHandle: true);
            if (set.IsInvalid) return ids;
            for (uint index = 0; ; index++)
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
                if (!PInvoke.SetupDiEnumDeviceInfo(set, index, ref data)) break;
                if (DeviceEnumerator.GetInstanceId(set, ref data) is { } id) ids.Add(id);
            }
        }
        return ids;
    }

    // ---------------------------------------------------------------- property reading

    private static unsafe JsonNode? ReadProperty(uint dev, Guid fmt, uint pid)
    {
        var key = new DEVPROPKEY { fmtid = fmt, pid = pid };
        uint size = 0;
        DEVPROPTYPE type;
        var cr = PInvoke.CM_Get_DevNode_Property(dev, key, out type, Span<byte>.Empty, ref size, 0);
        if ((uint)cr != CrBufferSmall || size == 0) return null;

        var buffer = new byte[size];
        if (PInvoke.CM_Get_DevNode_Property(dev, key, out type, buffer, ref size, 0) != CrSuccess) return null;
        return Decode((uint)type, buffer.AsSpan(0, (int)Math.Min(size, (uint)buffer.Length)));
    }

    private static JsonNode? Decode(uint type, ReadOnlySpan<byte> data)
    {
        const uint arrayModifier = 0x2000;
        var baseType = type & ~arrayModifier;

        if ((type & arrayModifier) != 0 && baseType == 0x12) // string list
            return ToArray(System.Text.Encoding.Unicode.GetString(data).Split('\0', StringSplitOptions.RemoveEmptyEntries));

        return baseType switch
        {
            0x12 or 0x13 or 0x19 => System.Text.Encoding.Unicode.GetString(data).TrimEnd('\0'),                    // string, SDDL, indirect
            0x07 when data.Length >= 4 => $"0x{BitConverter.ToUInt32(data):X8}",                                    // uint32 (hex: mostly flag fields)
            0x06 when data.Length >= 4 => BitConverter.ToInt32(data).ToString(),
            0x11 when data.Length >= 1 => data[0] != 0,                                                           // boolean
            0x0D when data.Length >= 16 => new Guid(data[..16]).ToString(),
            0x10 when data.Length >= 8 => FileTimeText(BitConverter.ToInt64(data)),                               // filetime
            _ => $"type 0x{type:X}: {Convert.ToHexString(data)}",
        };
    }

    private static string FileTimeText(long fileTime)
    {
        try { return fileTime <= 0 ? "none" : DateTime.FromFileTimeUtc(fileTime).ToString("O"); }
        catch (ArgumentOutOfRangeException) { return $"0x{fileTime:X}"; }
    }

    // ---------------------------------------------------------------- ids and interfaces

    private static unsafe string? GetId(uint dev)
    {
        Span<char> buffer = stackalloc char[MaxIdLength];
        fixed (char* p = buffer)
            return PInvoke.CM_Get_Device_IDW(dev, new PWSTR(p), (uint)buffer.Length, 0) == CrSuccess ? new string(p) : null;
    }

    private static string? GetParentId(uint dev) =>
        PInvoke.CM_Get_Parent(out var parent, dev, 0) == CrSuccess ? GetId(parent) : null;

    private static unsafe uint? Locate(string instanceId)
    {
        fixed (char* id = instanceId)
            return PInvoke.CM_Locate_DevNode(out var dev, new PWSTR(id), 0) == CrSuccess ? dev : null;
    }

    private static unsafe List<string> GetInterfaces(string instanceId, Guid interfaceGuid)
    {
        var paths = new List<string>();
        fixed (char* id = instanceId)
        {
            if (PInvoke.CM_Get_Device_Interface_List_Size(out var size, interfaceGuid, new PWSTR(id), 0) != CrSuccess || size <= 1)
                return paths;
            var buffer = new char[size];
            fixed (char* b = buffer)
            {
                if (PInvoke.CM_Get_Device_Interface_List(interfaceGuid, new PWSTR(id), new PZZWSTR(b), size, 0) != CrSuccess)
                    return paths;
            }
            paths.AddRange(new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries));
        }
        return paths;
    }

    // ---------------------------------------------------------------- Restart Manager

    /// <summary>
    /// Asks the Restart Manager which processes use each device interface path. It only reads: a session is
    /// opened, the path registered, the list read, and the session closed. Nothing is shut down or restarted.
    /// An empty result does not prove that nothing holds the device (RM is built for files), so errors are kept.
    /// </summary>
    private static JsonObject CaptureRestartManager(JsonObject tree)
    {
        var result = new JsonObject();
        foreach (var (id, node) in tree)
        {
            if (node?["interfaces"] is not JsonObject ifaces) continue;
            foreach (var group in new[] { "hid", "usbDevice" })
                foreach (var path in (ifaces[group] as JsonArray ?? []).Select(p => p?.GetValue<string>()).Where(p => p is not null))
                    result[path!] = QueryRestartManager(path!);
        }
        return result;
    }

    private static unsafe JsonNode QueryRestartManager(string path)
    {
        const uint errorMoreData = 234;
        var keyBuffer = new char[33]; // CCH_RM_SESSION_KEY + 1
        var start = (uint)PInvoke.RmStartSession(out var session, keyBuffer);
        if (start != 0) return new JsonObject { ["error"] = $"RmStartSession: {start}" };

        try
        {
            var files = new[] { path };
            var register = (uint)PInvoke.RmRegisterResources(session, files, default, default);
            if (register != 0) return new JsonObject { ["error"] = $"RmRegisterResources: {register}" };

            uint needed = 0, count = 0, reasons = 0;
            var rc = (uint)PInvoke.RmGetList(session, out needed, ref count, default, out reasons);
            var users = new JsonArray();
            if (rc == errorMoreData && needed > 0)
            {
                var infos = new Windows.Win32.System.RestartManager.RM_PROCESS_INFO[needed];
                count = needed;
                rc = (uint)PInvoke.RmGetList(session, out needed, ref count, infos, out reasons);
                if (rc == 0)
                    for (var i = 0; i < count; i++)
                        users.Add(new JsonObject
                        {
                            ["pid"] = (int)infos[i].Process.dwProcessId,
                            ["app"] = infos[i].strAppName.ToString(),
                            ["service"] = infos[i].strServiceShortName.ToString(),
                            ["type"] = infos[i].ApplicationType.ToString(),
                        });
            }
            return new JsonObject { ["result"] = (int)rc, ["users"] = users };
        }
        finally
        {
            PInvoke.RmEndSession(session);
        }
    }

    // ---------------------------------------------------------------- services, processes, commands

    private static JsonObject CaptureServices(JsonObject tree)
    {
        var wanted = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, node) in tree)
        {
            if (node?["properties"] is not JsonObject props) continue;
            if (props["service"]?.GetValue<string>() is { Length: > 0 } s) wanted.Add(s);
            foreach (var f in new[] { "upperFilters", "lowerFilters" })
                foreach (var item in props[f] as JsonArray ?? []) if (item?.GetValue<string>() is { } name) wanted.Add(name);
        }

        var result = new JsonObject();
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null) return result;
            foreach (var name in services.GetSubKeyNames())
            {
                using var key = services.OpenSubKey(name);
                if (key is null) continue;
                var image = key.GetValue("ImagePath")?.ToString();
                var display = key.GetValue("DisplayName")?.ToString();
                var matchesKeyword = Keywords.Any(k =>
                    name.Contains(k, StringComparison.OrdinalIgnoreCase)
                    || (image?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (display?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false));
                if (!matchesKeyword && !wanted.Contains(name)) continue;

                result[name] = new JsonObject
                {
                    ["reason"] = wanted.Contains(name) ? "bound to a controller devnode" : "name matches a vendor keyword",
                    ["displayName"] = display,
                    ["imagePath"] = image,
                    ["type"] = key.GetValue("Type")?.ToString(),
                    ["start"] = key.GetValue("Start")?.ToString(),
                    ["group"] = key.GetValue("Group")?.ToString(),
                    ["dependOnService"] = key.GetValue("DependOnService") is string[] d ? ToArray(d) : null,
                };
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            result["error"] = ex.Message;
        }
        return result;
    }

    private static JsonArray CaptureProcesses()
    {
        var list = new JsonArray();
        foreach (var p in Process.GetProcesses().OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            using (p)
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                if (!Keywords.Any(k => p.ProcessName.Contains(k, StringComparison.OrdinalIgnoreCase)
                                       || (path?.Contains(k, StringComparison.OrdinalIgnoreCase) ?? false))) continue;
                list.Add(new JsonObject { ["name"] = p.ProcessName, ["pid"] = p.Id, ["path"] = path });
            }
        }
        return list;
    }

    /// <summary>Raw output of read-only Windows tools, kept verbatim so nothing is lost by our own parsing.</summary>
    private static JsonObject CaptureCommands()
    {
        var result = new JsonObject();
        foreach (var (name, exe, args) in new (string, string, string[])[]
        {
            ("pnputilDrivers", "pnputil.exe", ["/enum-drivers"]),
            ("pnputilHidDevices", "pnputil.exe", ["/enum-devices", "/connected", "/class", "HIDClass", "/relations", "/drivers"]),
            ("pnputilUsbDevices", "pnputil.exe", ["/enum-devices", "/connected", "/class", "USB", "/relations", "/drivers"]),
            ("scDrivers", "sc.exe", ["query", "type=", "driver", "state=", "all"]),
            ("scServices", "sc.exe", ["query", "type=", "service", "state=", "all"]),
        })
            result[name] = RunTool(exe, args);
        return result;
    }

    private static JsonObject RunTool(string exe, string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) info.ArgumentList.Add(a);

            using var process = Process.Start(info);
            if (process is null) return new JsonObject { ["error"] = "could not start" };
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return new JsonObject { ["error"] = "timed out after 60 s" };
            }
            return new JsonObject { ["exitCode"] = process.ExitCode, ["output"] = stdout.Result, ["error"] = stderr.Result };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new JsonObject { ["error"] = ex.Message };
        }
    }

    // ---------------------------------------------------------------- helpers

    private static JsonArray ToArray(IEnumerable<string> items) => new(items.Select(i => (JsonNode?)JsonValue.Create(i)).ToArray());

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
