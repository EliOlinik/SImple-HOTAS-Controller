using System.ComponentModel;
using System.Runtime.InteropServices;
using HotasController.Core;
using HotasController.Core.Abstractions;
using HotasController.Core.Models;
using HotasController.Core.Safety;
using HotasController.Core.State;
using Serilog;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;

namespace HotasController.Win32;

/// <summary>
/// Enables / disables a whole game controller through SetupAPI (the mechanism Device Manager uses).
/// Needs an elevated process. Every call re-reads the controller, re-checks the safety policy,
/// verifies the resulting OS state, and records app-made disables in the state store.
/// </summary>
public sealed class DeviceToggler(IDeviceService devices, ISafetyPolicy safety, IDisabledStateStore store) : IDeviceToggler
{
    private const int ErrorAccessDenied = 5;
    private const uint DnNeedRestart = 0x00000100;
    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VerifyPoll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ScopeVerifyTimeout = TimeSpan.FromSeconds(1.5);

    public Task<ToggleResult> SetEnabledAsync(GameController controller, bool enabled, CancellationToken cancellationToken = default) =>
        Task.Run(() => Toggle(controller, enabled, cancellationToken), cancellationToken);

    private ToggleResult Toggle(GameController requested, bool enabled, CancellationToken ct)
    {
        var verb = enabled ? "enable" : "disable";
        Log.Information("Requested: {Verb} {Name} ({Id})", verb, requested.Name, requested.PhysicalDeviceInstanceId);
        try
        {
            var result = ToggleCore(requested, enabled, ct);
            if (result.Succeeded) Log.Information("Completed: {Verb} {Name}. {Message}", verb, requested.Name, result.Message);
            else Log.Warning("Failed: {Verb} {Name}: {Message}", verb, requested.Name, result.Message);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error while trying to {Verb} {Name}", verb, requested.Name);
            throw;
        }
    }

    private ToggleResult ToggleCore(GameController requested, bool enabled, CancellationToken ct)
    {
        // The caller's snapshot may be stale; always decide on the current OS state.
        var current = devices.GetGameControllers().FirstOrDefault(c =>
            string.Equals(c.PhysicalDeviceInstanceId, requested.PhysicalDeviceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (current is null) return ToggleResult.Fail("The controller is no longer connected.");

        var decision = safety.Evaluate(current, enabled ? DeviceAction.Enable : DeviceAction.Disable);
        if (!decision.Allowed) return ToggleResult.Fail(decision.Reason ?? "Not allowed.");

        // Only touch nodes that are not already in the wanted state. Companion interfaces (for example the vendor
        // WinUSB "Bulk" interface that keeps the light on) are part of the plan but are best-effort: the HID-level
        // nodes decide whether the toggle worked, so a stubborn companion never undoes a disable that stopped the input.
        var steps = ToggleTargets.Plan(current, enabled).Where(s => s.Node.IsDisabled == enabled).ToList();
        var changed = new List<(string InstanceId, uint DevInst)>();
        var changedPrimary = new List<(string InstanceId, uint DevInst)>();
        var companionProblems = new List<string>();

        // A node flagged "needs restart" is not a hard stop: with the X-56 vendor driver a disable sets the flag, and
        // Device Manager could still enable the node live. So log it and try anyway; the flag is only mentioned if
        // the attempt fails (see ToggleFailureText). Refusing up front also left a flagged stick impossible to toggle.
        var flagged = new List<string>();
        foreach (var step in steps)
        {
            if (Locate(step.Node.InstanceId) is { } devInst && (DeviceEnumerator.GetStatus(devInst).Status & DnNeedRestart) != 0)
            {
                Log.Warning("{Id} is flagged DN_NEED_RESTART before the change; trying anyway", step.Node.InstanceId);
                flagged.Add(step.Node.Name);
            }
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();
            var node = step.Node;
            var before = started.Elapsed;
            var (devInst, error) = ChangeStateWithFallback(node.InstanceId, enabled);
            Log.Information("Step {Kind} {Id}: {Result} in {Ms} ms", step.IsCompanion ? "companion" : "node", node.InstanceId,
                error is null ? "ok" : "failed", (int)(started.Elapsed - before).TotalMilliseconds);

            if (error is null)
            {
                changed.Add((node.InstanceId, devInst));
                if (!step.IsCompanion) changedPrimary.Add((node.InstanceId, devInst));
                continue;
            }

            if (step.IsCompanion)
            {
                Log.Warning("Companion {Id} could not be {Verb}d ({Error}); continuing", node.InstanceId, enabled ? "enable" : "disable", error);
                companionProblems.Add(node.Name);
                continue;
            }

            // Disabling is all-or-nothing: put back what we already switched off.
            Log.Warning("Change of {Id} failed ({Error}); rolling back {Count} node(s)", node.InstanceId, error, changed.Count);
            if (!enabled)
                foreach (var done in changed) ChangeStateWithFallback(done.InstanceId, true);
            return ToggleResult.Fail(ToggleFailureText.WithRestartHint($"{(enabled ? "Enabling" : "Disabling")} '{node.Name}' failed: {error}", flagged));
        }

        if (!WaitForState(changedPrimary, disabled: !enabled, out var timeline))
        {
            if (!enabled)
                foreach (var done in changed) ChangeStateWithFallback(done.InstanceId, true);
            Log.Warning("Verification failed for {Name}: {Timeline}", current.Name, timeline);
            return ToggleResult.Fail(ToggleFailureText.WithRestartHint($"Windows accepted the change, but the device did not switch state (a restart or another program may be holding it). Status timeline: {timeline}", flagged));
        }

        try
        {
            if (enabled)
                store.Remove(current.PhysicalDeviceInstanceId);
            else
                store.Add(new DisabledRecord(current.PhysicalDeviceInstanceId, current.Name, current.VendorId, current.ProductId, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not update the state file after {Name} changed state", current.Name);
            return new ToggleResult(true, $"{current.Name} {(enabled ? "enabled" : "disabled")}, but the state file could not be updated: {ex.Message}");
        }

        return new ToggleResult(true, $"{current.Name} {(enabled ? "enabled" : "disabled")}.");
    }

    /// <summary>
    /// Polls until every node reports the wanted state. <paramref name="timeline"/> records each change of the raw
    /// devnode status so a failure can show what Windows actually reported (and whether something flipped it back).
    /// </summary>
    private static bool WaitForState(List<(string InstanceId, uint DevInst)> nodes, bool disabled, out string timeline, TimeSpan? timeout = null)
    {
        var start = DateTime.UtcNow;
        var deadline = start + (timeout ?? VerifyTimeout);
        var log = new List<string>();
        var last = new Dictionary<uint, (bool, uint, uint)>();

        while (true)
        {
            foreach (var n in nodes)
            {
                var s = DeviceEnumerator.GetStatus(n.DevInst);
                if (!last.TryGetValue(n.DevInst, out var prev) || prev != s)
                {
                    last[n.DevInst] = s;
                    log.Add($"+{(int)(DateTime.UtcNow - start).TotalMilliseconds}ms {n.InstanceId}: ok={s.Ok} status=0x{s.Status:X} problem={s.Problem}");
                }
            }

            if (nodes.All(n => DeviceEnumerator.IsDisabled(n.DevInst) == disabled))
            {
                timeline = string.Join("; ", log);
                return true;
            }
            if (DateTime.UtcNow >= deadline)
            {
                timeline = string.Join("; ", log);
                return false;
            }
            Thread.Sleep(VerifyPoll);
        }
    }

    /// <summary>
    /// Switches one node and verifies the result. A failed call still counts as success when the node ended up in
    /// the wanted state.
    /// </summary>
    private static (uint DevInst, string? Error) ChangeStateWithFallback(string instanceId, bool enable) =>
        enable ? EnableBothScopes(instanceId) : DisableWithFallback(instanceId);

    /// <summary>
    /// A node can carry a global and a hardware-profile "disabled" flag, and an enable at one scope clears only that
    /// scope's flag (on the X-56 a single-scope enable reported OK and changed nothing, costing 1.5 s per node before
    /// the other scope was tried). So both scopes are issued back to back and the state is verified once.
    /// </summary>
    private static (uint DevInst, string? Error) EnableBothScopes(string instanceId)
    {
        uint devInst = 0;
        var errors = new List<string>();
        foreach (var scope in new[] { SETUP_DI_PROPERTY_CHANGE_SCOPE.DICS_FLAG_GLOBAL, SETUP_DI_PROPERTY_CHANGE_SCOPE.DICS_FLAG_CONFIGSPECIFIC })
        {
            var (inst, error) = ChangeState(instanceId, enable: true, scope);
            Log.Information("SetupAPI enable {Id} scope={Scope}: {Result}", instanceId, scope, error ?? "ok");
            if (inst != 0) devInst = inst;
            if (error is not null) errors.Add(error);
        }

        if (devInst != 0 && WaitForState([(instanceId, devInst)], disabled: false, out var seen, ScopeVerifyTimeout)) return (devInst, null);
        errors.Add($"state unchanged after both scopes ({(devInst == 0 ? "node not found" : "see log")})");
        return (devInst, string.Join(" / ", errors));
    }

    /// <summary>
    /// Disable uses the hardware-profile scope first (what devcon does) and falls back to global scope. Orderings
    /// were compared on the X-56 (issue #7): bulk-first and global-first gave no gain, so this stays the default.
    /// </summary>
    private static (uint DevInst, string? Error) DisableWithFallback(string instanceId)
    {
        uint devInst = 0;
        var errors = new List<string>();
        foreach (var scope in new[] { SETUP_DI_PROPERTY_CHANGE_SCOPE.DICS_FLAG_CONFIGSPECIFIC, SETUP_DI_PROPERTY_CHANGE_SCOPE.DICS_FLAG_GLOBAL })
        {
            var (inst, error) = ChangeState(instanceId, enable: false, scope);
            Log.Information("SetupAPI disable {Id} scope={Scope}: {Result}", instanceId, scope, error ?? "ok");
            if (inst != 0) devInst = inst;
            if (error is null)
            {
                // A reported success is not enough: some scopes return OK without changing anything. When the node
                // already shows "restart needed" and is not disabled, waiting longer does not help (the X-56 logs showed
                // the state never changing, then the next scope worked at once), so check once and move on.
                var stuck = devInst != 0 && !DeviceEnumerator.IsDisabled(devInst)
                            && (DeviceEnumerator.GetStatus(devInst).Status & DnNeedRestart) != 0;
                if (WaitForState([(instanceId, devInst)], disabled: true, out var seen, stuck ? TimeSpan.Zero : ScopeVerifyTimeout)) return (devInst, null);
                Log.Warning("Scope {Scope} reported OK for {Id} but the state did not change: {Timeline}", scope, instanceId, seen);
                errors.Add($"{scope}: reported OK but state unchanged ({seen})");
                continue;
            }
            errors.Add(error);
            // The installer can report an error after the state already changed.
            if (devInst != 0 && DeviceEnumerator.IsDisabled(devInst)) return (devInst, null);
        }
        return (devInst, string.Join(" / ", errors));
    }

    /// <summary>
    /// Returns the devnode handle on success, or an error message. HID-class nodes are found in the HID device set
    /// (the proven path); any other node, such as a vendor WinUSB interface, is opened directly by instance id.
    /// </summary>
    private static unsafe (uint DevInst, string? Error) ChangeState(string instanceId, bool enable, SETUP_DI_PROPERTY_CHANGE_SCOPE scope, bool dryRun = false)
    {
        var classGuid = DeviceEnumerator.HidClassGuid;
        var raw = PInvoke.SetupDiGetClassDevs(&classGuid, null, default, SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT);
        using var set = new SetupDiDestroyDeviceInfoListSafeHandle(raw.Value, ownsHandle: true);
        if (set.IsInvalid) return (0, "SetupDiGetClassDevs: " + Describe(Marshal.GetLastPInvokeError()));

        for (uint index = 0; ; index++)
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
            if (!PInvoke.SetupDiEnumDeviceInfo(set, index, ref data)) break;

            var id = DeviceEnumerator.GetInstanceId(set, ref data);
            if (!string.Equals(id, instanceId, StringComparison.OrdinalIgnoreCase)) continue;
            return ApplyPropertyChange(set, ref data, enable, scope, dryRun);
        }

        return ChangeStateById(instanceId, enable, scope, dryRun);
    }

    private static unsafe (uint DevInst, string? Error) ChangeStateById(string instanceId, bool enable, SETUP_DI_PROPERTY_CHANGE_SCOPE scope, bool dryRun)
    {
        Guid? noClass = null;
        using var set = PInvoke.SetupDiCreateDeviceInfoList(noClass, default(Windows.Win32.Foundation.HWND));
        if (set.IsInvalid) return (0, "SetupDiCreateDeviceInfoList: " + Describe(Marshal.GetLastPInvokeError()));

        var data = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
        if (!PInvoke.SetupDiOpenDeviceInfo(set, instanceId, default, 0, ref data))
            return (0, "device node not found (" + Describe(Marshal.GetLastPInvokeError()) + ").");
        return ApplyPropertyChange(set, ref data, enable, scope, dryRun);
    }

    private static unsafe (uint DevInst, string? Error) ApplyPropertyChange(
        SetupDiDestroyDeviceInfoListSafeHandle set, ref SP_DEVINFO_DATA data, bool enable, SETUP_DI_PROPERTY_CHANGE_SCOPE scope, bool dryRun)
    {
        var parameters = new SP_PROPCHANGE_PARAMS
        {
            ClassInstallHeader = new SP_CLASSINSTALL_HEADER
            {
                cbSize = (uint)sizeof(SP_CLASSINSTALL_HEADER),
                InstallFunction = DI_FUNCTION.DIF_PROPERTYCHANGE,
            },
            StateChange = enable ? SETUP_DI_STATE_CHANGE.DICS_ENABLE : SETUP_DI_STATE_CHANGE.DICS_DISABLE,
            Scope = scope,
            HwProfile = 0,
        };

        var hdev = new HDEVINFO(set.DangerousGetHandle());
        fixed (SP_DEVINFO_DATA* d = &data)
        {
            if (!PInvoke.SetupDiSetClassInstallParams(hdev, d, &parameters.ClassInstallHeader, (uint)sizeof(SP_PROPCHANGE_PARAMS)))
                return (0, $"SetupDiSetClassInstallParams (size {sizeof(SP_PROPCHANGE_PARAMS)}, devinfo {d->cbSize}): " + Describe(Marshal.GetLastPInvokeError()));
            if (dryRun) return (d->DevInst, null);
            if (!PInvoke.SetupDiCallClassInstaller(DI_FUNCTION.DIF_PROPERTYCHANGE, hdev, d))
                return (d->DevInst, $"SetupDiCallClassInstaller ({scope}): " + Describe(Marshal.GetLastPInvokeError()));
            return (d->DevInst, null);
        }
    }

    private static unsafe uint? Locate(string instanceId)
    {
        fixed (char* id = instanceId)
            return PInvoke.CM_Locate_DevNode(out var devInst, new Windows.Win32.Foundation.PWSTR(id), 0) == 0 ? devInst : null;
    }

    /// <summary>Diagnostic: runs everything up to (not including) the state change. Does not modify any device.</summary>
    public static unsafe string Probe(string instanceId)
    {
        var (_, error) = ChangeState(instanceId, enable: false, SETUP_DI_PROPERTY_CHANGE_SCOPE.DICS_FLAG_CONFIGSPECIFIC, dryRun: true);
        return (error ?? "SetupDiSetClassInstallParams accepted the parameters (no device was changed).")
               + $" [sizeof SP_DEVINFO_DATA={sizeof(SP_DEVINFO_DATA)}, SP_PROPCHANGE_PARAMS={sizeof(SP_PROPCHANGE_PARAMS)}, SP_CLASSINSTALL_HEADER={sizeof(SP_CLASSINSTALL_HEADER)}]";
    }

    private static string Describe(int error) =>
        error == ErrorAccessDenied
            ? "access denied - run the app as administrator."
            : new Win32Exception(error).Message;
}
