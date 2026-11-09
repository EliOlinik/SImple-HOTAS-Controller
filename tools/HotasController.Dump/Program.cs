using HotasController.Core;
using HotasController.Win32;

// ---- Snapshot and compare (read-only; for the driver investigation). ----
//   dotnet run --project tools/HotasController.Dump -- --snapshot scratchpad/snapshots/generic.json [label]
//   dotnet run --project tools/HotasController.Dump -- --diff scratchpad/snapshots/generic.json scratchpad/snapshots/vendor.json
// Snapshots hold real instance ids and process paths: keep them in the gitignored scratchpad, never commit them.
if (args.Contains("--snapshot"))
{
    var rest = args.SkipWhile(a => a != "--snapshot").Skip(1).ToArray();
    if (rest.Length == 0) { Console.WriteLine("Give an output file, e.g. --snapshot scratchpad/snapshots/generic.json [label]"); return; }
    var file = Path.GetFullPath(rest[0]);
    var snapshot = DeviceSnapshot.Capture(rest.Length > 1 && !rest[1].StartsWith("--") ? rest[1] : Path.GetFileNameWithoutExtension(file), VendorArgs(args));
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, snapshot.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Snapshot written to {file}");
    Console.WriteLine($"  elevated: {snapshot["elevated"]}  controllers: {(snapshot["controllers"] as System.Text.Json.Nodes.JsonObject)?.Count}  devnodes: {(snapshot["devnodes"] as System.Text.Json.Nodes.JsonObject)?.Count}");
    if (snapshot["elevated"]?.GetValue<bool>() != true) Console.WriteLine("  note: not elevated, some details (services, Restart Manager) may be incomplete. Prefer an elevated terminal.");
    return;
}

if (args.Contains("--diff"))
{
    var rest = args.SkipWhile(a => a != "--diff").Skip(1).ToArray();
    if (rest.Length < 2) { Console.WriteLine("Give two snapshot files: --diff before.json after.json"); return; }
    var differences = HotasController.Core.Diagnostics.SnapshotDiff.Compare(File.ReadAllText(rest[0]), File.ReadAllText(rest[1]));
    Console.WriteLine($"{differences.Count} difference(s)  (before: {rest[0]}, after: {rest[1]})");
    foreach (var d in differences)
    {
        var text = d.Kind switch
        {
            "added" => $"+ {d.Path} = {Shorten(d.After)}",
            "removed" => $"- {d.Path} = {Shorten(d.Before)}",
            _ => $"~ {d.Path}: {Shorten(d.Before)}  ->  {Shorten(d.After)}",
        };
        Console.WriteLine(text);
    }
    return;

    static string Shorten(string? s) => s is null ? "" : s.Length > 160 ? s[..160].Replace("\n", "\\n") + "..." : s.Replace("\n", "\\n");
}

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
var companionSource = new DeviceEnumerator();
var controllers = GameControllerGrouper.Group(nodes, store.Load().Select(r => r.PhysicalDeviceInstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase))
    .Select(c => c with { CompanionNodes = companionSource.GetCompanionNodes(c.PhysicalDeviceInstanceId, c.Nodes.Select(n => n.InstanceId).ToHashSet(StringComparer.OrdinalIgnoreCase)) })
    .ToList();
if (store.LoadProblem is { } storeProblem)
    Console.WriteLine($"\nWARNING: {storeProblem}\n(The data folder is limited to administrators; run this from an elevated terminal to read it.)");
Console.WriteLine();
Console.WriteLine($"== Game controllers ({controllers.Count}) ==");
foreach (var c in controllers)
{
    var vidpid = c.VendorId is { } v && c.ProductId is { } p ? $"{v:X4}:{p:X4}" : "----:----";
    Console.WriteLine($"{c.Name}  [{vidpid}]  enabled={c.IsEnabled}  keyboard/mouse={c.ExposesKeyboardOrMouse}  nodes={c.Nodes.Count} companions={c.CompanionNodes.Count}{(c.RecognisedFromState ? "  (known from state file)" : "")}");
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
    // The toggler logs every step with timings; show them here so each experiment run is self-explanatory.
    // --log <file> also appends the steps to a file (with the command line), so runs can be compared afterwards.
    var logFile = FlagValue(args, "--log");
    if (logFile is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logFile))!);
        File.AppendAllText(logFile, $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} dump {string.Join(' ', args)}{Environment.NewLine}");
    }
    Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(new ConsoleSink(logFile)).CreateLogger();
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

// --vid 0738 (repeatable): also snapshot USB devices of that vendor even when they are disabled and so not listed.
static IEnumerable<string> VendorArgs(string[] args) =>
    args.Select((a, i) => (a, i)).Where(t => t.a == "--vid" && t.i + 1 < args.Length).Select(t => args[t.i + 1]);

static string? FlagValue(string[] args, string flag) =>
    args.Select((a, i) => (a, i)).Where(t => t.a == flag && t.i + 1 < args.Length).Select(t => args[t.i + 1]).FirstOrDefault();

sealed class ConsoleSink(string? file) : Serilog.Core.ILogEventSink
{
    public void Emit(Serilog.Events.LogEvent logEvent)
    {
        var line = $"  [{logEvent.Timestamp:HH:mm:ss.fff}] {logEvent.RenderMessage()}";
        Console.WriteLine(line);
        if (file is not null) File.AppendAllText(file, line + Environment.NewLine);
    }
}
