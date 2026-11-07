# Decisions (append-only)

Format: `date – decision – reason`

- 2026-11-07 – C# / .NET 8 / WPF – best Win32 interop (CsWin32), quick UI, single-file publish.
- 2026-11-07 – Game controllers only; never list or touch other HID devices – user requirement, and reduces lockout risk.
- 2026-11-07 – Disable the whole physical device (all HID nodes under the same parent) – HOTAS units expose multiple nodes. Composite devices with keyboard/mouse usages are refused.
- 2026-11-07 – CLI-only scaffolding for now – user will open the solution in VS Code later.
- 2026-11-07 – No git init – user will create the repo later.
- 2026-11-07 – Enumerate the HIDClass setup class (includes the "USB Input Device" nodes), not only HID device interfaces – disabled devices have no readable HID interface, but their devnodes still exist, so status and name stay available.
- 2026-11-07 – Physical device = nearest ancestor that is not a USB composite-interface (`&MI_xx`) node; the controller name is the USB product string (DEVPKEY_Device_BusReportedDeviceDesc), falling back to the node name.
- 2026-11-07 – A node with unreadable usage counts as a game controller only if Windows names it "game controller" (needed to keep disabled controllers visible so they can be re-enabled).
- 2026-11-07 – DeviceSafetyPolicy checks empty Nodes first (IsFullyDisabled/IsEnabled are vacuously true on empty). Enable is allowed on a keyboard/mouse composite (only Disable is blocked).
- 2026-11-07 T07: DisabledStateStore is locked per instance only; DI should register a single instance. Write errors propagate (callers must handle); Load swallows errors. Clear() writes an empty JSON array rather than deleting the file.
- 2026-11-07 T09: App has no StartupUri; App.OnStartup builds MainWindow from App.CreateMainViewModel() (the only composition spot). Confirmation is asked only when disabling, after the safety check. Fakes share one in-memory state (toggler takes FakeDeviceService).
- 2026-11-07 – Toggle target = the USB-level nodes of a controller (`USB\...` "USB Input Device"), falling back to all nodes if none (e.g. Bluetooth); disabling them takes down the HID children, like Device Manager. Logic in `Core/ToggleTargets`, unit-tested.
- 2026-11-07 – `DeviceToggler` re-reads the controller and re-runs the safety policy on every call (UI snapshots can be stale), verifies the OS state for up to 5 s, rolls back a partial disable, and updates `IDisabledStateStore` only after success.
- 2026-11-07 – Hardware test: PnP disable of the DualSense's USB-level node failed while Steam/joy.cpl had it open and left a pending-restart flag. Toggler now refuses nodes with DN_NEED_RESTART and verifies each scope; retest after reboot with Steam and joy.cpl closed before deciding on a different target node.
- 2026-11-07 – A disabled controller is recognised only via the state file (`RecognisedFromState`): Windows removes the HID child when the USB-level node is disabled, so usage/name are unavailable. Trade-off: devices disabled outside the app are not listed while disabled.
- 2026-11-07 – Hot-plug uses the WM_DEVICECHANGE window message (DBT_DEVNODES_CHANGED / arrival / removal) with a 700 ms debounce in `MainWindow`, instead of CM_Register_Notification: no unmanaged callbacks, no extra bindings, and a top-level window receives it automatically. Changes during a running operation are ignored (the operation reloads itself).
- 2026-11-07 – State file self-heals: `DeviceService` prunes records of controllers that are present and fully enabled (`StateReconciler`); unplugged controllers keep their record. At startup `MainViewModel.OfferRecoveryAsync` asks whether to re-enable controllers an earlier session left disabled.
- 2026-11-07 – Startup "re-enable?" popup removed at the user's request (and `OfferRecoveryAsync` deleted). Controllers left disabled by an earlier session simply show as Disabled with the "Re-enable all" button visible; stale state records are still pruned automatically.
- 2026-11-07 – Logging: Serilog static `Log` (Serilog package added to Win32 too) writing daily-rolling files to `%ProgramData%\HotasController\logs\hotas-YYYYMMDD.log`, 14 files kept. Every toggle logs request, each SetupAPI scope attempt, verification problems, rollback and result. Logs contain device instance IDs (serials), local only.
- 2026-11-07 – Unhandled UI exceptions are logged and shown in a dialog (app keeps running); non-UI unhandled and unobserved task exceptions are logged. Packaging via `publish.ps1` (win-x64, self-contained, single file, compressed, no pdb) instead of a RuntimeIdentifier in the csproj, which would move the Debug output folder.
