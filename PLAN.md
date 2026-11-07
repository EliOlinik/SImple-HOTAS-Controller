# HOTAS Controller – Implementation Plan

Small Windows helper app that lists connected **game controllers** (HOTAS, joysticks, gamepads) and lets the user disable / re-enable each one.

## Decisions (confirmed)

- **Scope: game controllers only.** The tool never lists or touches keyboards, mice, or other HID devices.
- **Stack:** C# / .NET 8 (`net8.0-windows`), WPF UI, x64.
- **Disable granularity:** the whole physical device (all HID nodes under the same parent USB device), because HOTAS units usually expose several nodes.
- **Tooling:** CLI only for now (`dotnet` CLI). VS Code can open the solution later.
- **Git:** not initialised yet. The user will create the repo later. Do not run `git init` or commit.

## Tooling

| Need | Choice |
|---|---|
| SDK | .NET 8 LTS (8.0.425 installed; default `dotnet` is 9.0.308, so pin with `global.json` to 8.0.x) |
| UI | WPF + `CommunityToolkit.Mvvm` |
| Win32 | `Microsoft.Windows.CsWin32` (SetupAPI, CfgMgr32, HID) |
| Logging | `Serilog` + file sink -> `%ProgramData%\HotasController\logs` |
| Tests | xUnit; device layer behind an interface, fake in tests |
| Packaging | `dotnet publish` self-contained single-file x64; `app.manifest` = `requireAdministrator` |
| Cross-check | `pnputil /enum-devices /connected`, Device Manager |

## Solution layout

```
HOTAS_Controller/
  PLAN.md
  global.json
  HotasController.sln
  scratchpad/                      # multi-agent workspace (see scratchpad/README.md)
  src/
    HotasController.Core/          # no UI, no Win32 types leaking out
      Models/DeviceInfo.cs         # InstanceId, Name, VID/PID, ParentId, IsEnabled, Usages
      Abstractions/IDeviceService.cs
      Safety/DeviceSafetyPolicy.cs
      State/DisabledStateStore.cs  # JSON in %ProgramData%
    HotasController.Win32/         # CsWin32 + implementations
      DeviceEnumerator.cs          # SetupDi + CfgMgr32
      DeviceToggler.cs             # DICS_DISABLE / DICS_ENABLE
      HidUsageReader.cs            # HidD/HidP usage page + usage
      DeviceWatcher.cs             # CM_Register_Notification hot-plug
    HotasController.App/           # WPF, app.manifest (requireAdministrator)
      ViewModels/  Views/  App.xaml
  tests/HotasController.Tests/
  tools/HotasController.Dump/      # read-only console dump of HID nodes and game controllers
```

## Phases

### Phase 1 – Scaffold
- `global.json`, solution, Core / Win32 / App / Tests projects, package references, manifest.
- Milestone: empty WPF window launches elevated; `dotnet build` and `dotnet test` pass.

### Phase 2 – Enumeration (read-only)
- `SetupDiGetClassDevs` on the HID class with `DIGCF_PRESENT`.
- Per node: instance ID, friendly name, hardware IDs (VID/PID), parent via `CM_Get_Parent`, status via `CM_Get_DevNode_Status` (disabled = `DN_HAS_PROBLEM` + `CM_PROB_DISABLED`).
- Open each HID interface with zero access; read top-level usage page/usage via `HidD_GetPreparsedData` + `HidP_GetCaps`.
- Keep only Generic Desktop (0x01) usages Joystick (0x04), Game Pad (0x05), Multi-axis (0x08).
- Group nodes by parent USB device.
- Milestone: console dump matches Device Manager / `pnputil` for the user's HOTAS.

### Phase 3 – Safety policy (before any toggling)
- Lives in Core, unit-tested.
- Refuse any device group that also exposes keyboard or mouse usages (composite devices).
- Refuse anything that is not a game controller.
- Re-enable allowed for app-disabled devices and any currently disabled game controller.

### Phase 4 – Enable / disable
- `SetupDiSetClassInstallParams` (`DICS_DISABLE` / `DICS_ENABLE`, `DICS_FLAG_GLOBAL`) then `SetupDiCallClassInstaller(DIF_PROPERTYCHANGE)`.
- Off the UI thread, with timeout and debounce.
- Re-read status afterwards to verify; report the real outcome.
- Record every disable in `DisabledStateStore`.

### Phase 5 – UI
- Grouped controller list: name, VID:PID, connected indicator, enable/disable toggle.
- Confirmation dialog on disable.
- "Re-enable everything I disabled" button; refresh; status bar for errors.

### Phase 6 – Hot-plug and recovery
- `CM_Register_Notification` for plug / unplug.
- Startup recovery check: offer to re-enable devices the state file says are still disabled.

### Phase 7 – Hardening and packaging
- Log every action; error-handling polish.
- Self-contained single-file publish; test on a clean setup.
- Code signing optional (only if distributed).

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Disabling the wrong device (keyboard/mouse lockout) | Strict game-controller filter; refuse composite devices with keyboard/mouse usages; confirm on disable |
| Elevated process | Small codebase, no plugins, no DLL probing, protected install location; later maybe split UI / elevated helper |
| Disabled state persists after crash / uninstall | State file, "re-enable all", startup recovery, mention Device Manager as fallback |
| Game or vendor software holds the device / re-enables it | Warn the user; treat reverted or failed result as a normal outcome |
| Driver instability on rapid toggling | Off-UI-thread with timeouts; debounce |
| AV / policy / anti-cheat suspicion | No hooks or injection; sign the exe if distributed |
| Privacy (serials, hardware IDs) | Keep logs local; no telemetry |

## Testing

- **Unit:** safety policy, grouping, state store, using a fake `IDeviceService`.
- **Manual with hardware:** enumerate, disable, re-enable, replug while disabled, behaviour with a game open, composite device refused.
- First toggle tests on a non-essential controller, never the only keyboard or mouse.

## Status

- [x] Framework and plan agreed
- [x] Phase 1 – Scaffold (build + test pass; elevated launch not yet checked by the user)
- [x] Phase 2 – Enumeration (34 HID nodes match Get-PnpDevice; 3 controllers found; disabled-state path untested on real hardware)
- [x] Phase 3 – Safety policy
- [x] Phase 4 – Enable / disable (verified on DualSense and X-56 by the user, with Steam and joy.cpl open)
- [x] Phase 5 – UI (real services wired, re-enable-all added; user launch test pending)
- [x] Phase 6 – Hot-plug and recovery (user test pending)
- [x] Phase 7 – Hardening and packaging (publish.ps1 produces a 68 MB single exe; logging not yet seen running)
