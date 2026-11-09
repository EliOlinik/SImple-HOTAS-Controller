using HotasController.Core;
using HotasController.Win32;

// Read-only diagnostic: prints every HID node and the game controllers derived from them.
// Compare against: pnputil /enum-devices /connected /class HIDClass
var nodes = new DeviceEnumerator().EnumerateHidNodes();

Console.WriteLine($"== HID nodes ({nodes.Count}) ==");
foreach (var n in nodes.OrderBy(n => n.PhysicalDeviceInstanceId).ThenBy(n => n.InstanceId))
{
    var usage = n.Usage is { } u ? $"{u.Page:X4}:{u.Usage:X4}" : "----:----";
    var vidpid = n.VendorId is { } v && n.ProductId is { } p ? $"{v:X4}:{p:X4}" : "----:----";
    Console.WriteLine($"{(n.IsDisabled ? "OFF" : "on ")} usage={usage} vid:pid={vidpid}  {n.Name}");
    Console.WriteLine($"      {n.InstanceId}");
    Console.WriteLine($"      parent: {n.PhysicalDeviceInstanceId}");
}

var store = new HotasController.Core.State.DisabledStateStore(HotasController.Core.State.DisabledStateStore.DefaultPath);
var controllers = GameControllerGrouper.Group(nodes, store.Load().Select(r => r.PhysicalDeviceInstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase));
if (store.LoadProblem is { } storeProblem)
    Console.WriteLine($"\nWARNING: {storeProblem}\n(The data folder is limited to administrators; run this from an elevated terminal to read it.)");
Console.WriteLine();
Console.WriteLine($"== Game controllers ({controllers.Count}) ==");
foreach (var c in controllers)
{
    var vidpid = c.VendorId is { } v && c.ProductId is { } p ? $"{v:X4}:{p:X4}" : "----:----";
    Console.WriteLine($"{c.Name}  [{vidpid}]  enabled={c.IsEnabled}  keyboard/mouse={c.ExposesKeyboardOrMouse}  nodes={c.Nodes.Count}{(c.RecognisedFromState ? "  (known from state file)" : "")}");
    Console.WriteLine($"      {c.PhysicalDeviceInstanceId}");
}

// ---- Manual toggle test (T08). Only runs when asked; never part of automated runs. ----
//   dotnet run --project tools/HotasController.Dump -- --disable "DualSense"
//   dotnet run --project tools/HotasController.Dump -- --enable  "DualSense"
// Needs an elevated terminal. Asks for explicit confirmation before touching the device.
var cmd = args.FirstOrDefault(a => a is "--disable" or "--enable");
if (cmd is not null)
{
    var filter = args.SkipWhile(a => a != cmd).Skip(1).FirstOrDefault();
    var enable = cmd == "--enable";
    if (string.IsNullOrWhiteSpace(filter)) { Console.WriteLine("Give part of the controller name, e.g. --disable \"DualSense\"."); return; }

    var matches = controllers.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
    if (matches.Count != 1)
    {
        Console.WriteLine($"\n'{filter}' matched {matches.Count} controllers; it must match exactly one.");
        return;
    }

    var target = matches[0];
    var toggler = new DeviceToggler(new DeviceService(store), new HotasController.Core.Safety.DeviceSafetyPolicy(), store);

    Console.WriteLine($"\nAbout to {(enable ? "ENABLE" : "DISABLE")}: {target.Name}  ({target.PhysicalDeviceInstanceId})");
    Console.Write("Type YES to continue: ");
    if (Console.ReadLine()?.Trim() != "YES") { Console.WriteLine("Cancelled."); return; }

    var result = await toggler.SetEnabledAsync(target, enable);
    Console.WriteLine(result.Succeeded ? $"OK: {result.Message}" : $"FAILED: {result.Message}");
    Console.WriteLine($"State file: {HotasController.Core.State.DisabledStateStore.DefaultPath}  ({store.Load().Count} record(s))");
}

// Non-mutating diagnostic: validates the SetupAPI parameters for a controller without changing it.
//   dotnet run --project tools/HotasController.Dump -- --probe "DualSense"
if (args.Contains("--probe"))
{
    var filter = args.SkipWhile(a => a != "--probe").Skip(1).FirstOrDefault() ?? "";
    foreach (var c in controllers.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        foreach (var n in HotasController.Core.ToggleTargets.Select(c))
            Console.WriteLine($"probe {n.InstanceId}: {DeviceToggler.Probe(n.InstanceId)}");
}
