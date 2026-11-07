# Task board

Status: `todo` | `in-progress` | `blocked` | `done`. Claim a task by setting Owner and status before starting.

| ID | Phase | Task | Owner | Status | Depends on | Files |
|---|---|---|---|---|---|---|
| T01 | 1 | Scaffold: `global.json`, solution, 4 projects, packages, manifest | main | done | - | `global.json`, `*.sln`, `src/*`, `tests/*` |
| T02 | 2 | Core models + `IDeviceService` interface | main | done | T01 | `src/HotasController.Core/Models`, `Abstractions` |
| T03 | 2 | Win32 `DeviceEnumerator` (SetupDi + CfgMgr32), read-only | main | done | T01, T02 | `src/HotasController.Win32/DeviceEnumerator.cs` |
| T04 | 2 | Win32 `HidUsageReader` (usage page/usage) | main | done | T01 | `src/HotasController.Win32/HidUsageReader.cs` |
| T05 | 2 | Group nodes by parent device + console dump check vs `pnputil` | main | done | T03, T04 | `Core`, `findings/` |
| T06 | 3 | `DeviceSafetyPolicy` + unit tests | agent-T06 | done | T02 | `src/HotasController.Core/Safety`, `tests/` |
| T07 | 4 | `DisabledStateStore` (JSON) + tests | agent-T07 | done | T02 | `src/HotasController.Core/State`, `tests/` |
| T08 | 4 | `DeviceToggler` (DICS_DISABLE/ENABLE, verify, timeout) | main | done | T05, T06, T07 | `src/HotasController.Win32/DeviceToggler.cs` |
| T09 | 5 | WPF shell + view models on fake `IDeviceService` | agent-T09 | done | T02 | `src/HotasController.App` |
| T10 | 5 | Wire UI to real services; confirm dialog; re-enable-all | main | done (UI test pending) | T08, T09 | `src/HotasController.App` |
| T11 | 6 | `DeviceWatcher` hot-plug + startup recovery | main | done (UI test pending) | T08 | `src/HotasController.Win32/DeviceWatcher.cs` |
| T12 | 7 | Serilog logging, error handling, single-file publish | main | done (run test pending) | T10 | `src/*`, publish profile |
