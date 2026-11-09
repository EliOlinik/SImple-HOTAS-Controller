# HOTAS Controller: notes for agents

## Introduction

**What it is:** a small Windows desktop utility that lists the game controllers plugged into the machine (joysticks, gamepads, HOTAS sticks and throttles, over USB or Bluetooth) and lets the user switch each one on or off, and save sets of those states as presets. Read `README.md` for the user-facing description. This file records how to work in the repo and the lessons learned building it.

**Scope:**
- In scope: enumerating HID game controllers (joystick, gamepad and multi-axis usages), enabling and disabling them through the Windows device-install API, remembering which ones the app disabled, saving and applying presets, hot-plug refresh, logging, and a single-file publish.
- Out of scope: keyboards, mice and other peripherals (the safety policy refuses them), installing or changing drivers, input remapping, calibration, telemetry or any network access, and non-Windows platforms.
- It is a single-user tool for one machine. It needs administrator rights and has no installer.

**Technologies and frameworks**

| Area | Choice |
|---|---|
| Language and runtime | C# on .NET 8 (SDK pinned to 8.0.425 in `global.json`) |
| UI | WPF (`net8.0-windows`), MVVM with CommunityToolkit.Mvvm 8.4.2 |
| Native access | Win32 SetupAPI, CfgMgr32 and HID APIs, called through source-generated P/Invoke from Microsoft.Windows.CsWin32 0.3.369 (x64 only) |
| Logging | Serilog 4.4.0 with Serilog.Sinks.File 7.0.0 |
| Persistence | JSON files in `%ProgramData%\HotasController\` (`System.Text.Json`) |
| Tests | xUnit (`tests/HotasController.Tests`) |
| Packaging | `dotnet publish` single file, self-contained, win-x64, wrapped by `publish.cmd` / `publish.ps1` |
| Source control | Git on GitHub (`EliOlinik/SImple-HOTAS-Controller`), protected `main`, PR workflow, `gh` CLI |

The repository is public and MIT licensed.

## Ground rules

- **Never toggle real hardware.** Only the user enables or disables real devices. Use the fakes (`--fake`), the unit tests and read-only enumeration (`tools/HotasController.Dump`). The Dump tool's `--disable`, `--enable` and `--probe` flags exist for the user to run, not for you.
- **Never kill the user's running processes** (for example `HotasController.App.exe`) to free a file lock. Ask them to close it.
- **Ask before anything outward-facing or hard to reverse:** merging a PR, deleting remote branches, force-pushing, making the repo public or private. Committing and pushing a feature branch is fine when asked.
- **Privacy:** never commit real device serial numbers, Bluetooth addresses or machine paths. Tests use fake ids such as `SN0001` and `AABBCCDDEEFF`.
- **Be honest in PR text and reports.** Say what was tested on hardware and what was not. Do not claim the UI was checked unless the user said so.

## Project layout and layering

```
src/HotasController.Core    net8.0          pure logic, models, interfaces. No Win32 and no UI types.
src/HotasController.Win32   net8.0-windows  SetupAPI / CfgMgr32 / HID via CsWin32. Implements Core's interfaces.
src/HotasController.App     net8.0-windows  WPF + MVVM. Composition root is App.CreateMainViewModel.
tests/HotasController.Tests xUnit, references Core only
tools/HotasController.Dump  read-only console diagnostic (plus guarded manual toggle flags)
```

- Dependencies point inward: `App` -> `Win32` and `Core`; `Win32` -> `Core`; `Core` -> nothing.
- Put decision logic in `Core` as pure functions so it can be unit tested without hardware. The UI and Win32 layers stay thin. Examples: `GameControllerGrouper`, `DeviceSafetyPolicy`, `PresetPlanner`, `InstanceIdParser`, `StateReconciler`.
- View models never touch the UI directly. User interaction goes through interfaces (`IConfirmationService`, `IPromptService`) so they can be faked.
- `--fake` mode runs the whole UI on in-memory fakes (`App/Fakes`). Keep the fakes in step with new features (for example the fake Bluetooth controller) so the UI can be tried without hardware.

## Build, test, run

- `global.json` pins SDK 8.0.425. The machine default is newer, so rely on the pin.
- `dotnet build HotasController.sln` and `dotnet test HotasController.sln`. Keep **0 warnings and 0 errors** and all tests green before committing.
- Projects that use CsWin32 must set `<PlatformTarget>x64</PlatformTarget>`. AnyCPU fails with `PInvoke005`.
- The app manifest requires administrator rights. `dotnet run --project src/HotasController.App` needs an **elevated terminal**, otherwise it fails with Win32 error 740. You cannot launch it yourself, so ask the user to run it.
- `publish.cmd` produces the single-file, self-contained exe in `dist/`. It wraps `publish.ps1` with `-ExecutionPolicy Bypass` because the PowerShell execution policy blocks `.\publish.ps1`. The exe is about 65 MB because it carries the .NET runtime and WPF.
- **Do not build into a folder the user is running from.** A running exe locks its files (`MSB3021` / `MSB3027`). Build into the `dist` folder. If the running app blocks the build, **ask the user to close it**; do not build elsewhere or work around it.

## Code standards

- `Nullable` and `ImplicitUsings` are enabled. Do not suppress nullable warnings casually. Fix the type or use `!` only where the invariant is obvious.
- Models are immutable `record`s (`GameController`, `HidNode`, `Preset`, `PresetEntry`).
- Comments explain **why**, not what. Public types get a short `///` summary.
- View models use CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`, `[NotifyCanExecuteChangedFor]`).
- **JSON stores** (`DisabledStateStore`, `PresetStore`) follow one pattern: thread-safe within a process, atomic write (temp file then move), never throw on load. They live under `%ProgramData%\HotasController\`. Share one store instance per file.
- **Data files are untrusted input** (the app runs elevated). Read and write them only through `SafeJsonFile` and validate with `DataFileValidator`:
  - Limits live in `DataFileLimits`: 100 presets, 1000 devices (per preset and in the disabled list), 4 MB per file, name lengths, no control characters. Change a limit there, not in the stores.
  - A file that is too large, is not valid UTF-8, is not the expected JSON, breaks a limit or has duplicates is **rejected as a whole**, never partly used. The store returns an empty list and sets `LoadProblem` with a user-readable message. A copy is kept as `*.corrupt` (not for files that are too large or are links).
  - Surface `LoadProblem` to the user (`MainViewModel.DataWarning` is the permanent banner). Never silently treat a bad file as "no data".
  - What the app writes must satisfy what it accepts: names are cleaned with `DataFileLimits.CleanName`, the disabled list drops its oldest record at 1000, and `PresetStore.Save` refuses a 101st preset.
  - Never write through a link: `SafeJsonFile.WriteAtomic` refuses a directory or file that is a reparse point.
- **Data folder:** `DataFolder.Prepare` (Win32) runs in `App.OnStartup` **before logging starts**, because Serilog would otherwise create the folder with default permissions. It creates the folder with an access list for Administrators and SYSTEM only, refuses an owner that is not Administrators or SYSTEM, and refuses links inside. Anything new that writes under the data folder must go through this folder, and any new folder or file created there needs no extra permission handling because it inherits the list.
- **Logging:** Serilog static `Log`. Daily file under `%ProgramData%\HotasController\logs\`, 14 kept. Unhandled exceptions are logged and shown to the user.
- **Tests:** xUnit, one test file per Core class, names like `Match_VidPidFallback_RefusedWhenTwoIdenticalControllersPlugged`. Cover the ambiguous cases, not just the happy path. New behaviour in `Core` needs tests.
- **README:** every user-visible feature updates `README.md` in the same change.
- **Confirmations:** actions that overwrite or delete saved data confirm: Update preset, Delete preset, Forget. A single checkbox click, Apply and Re-enable all do not (they are easy to undo).
- Safety first: `DeviceSafetyPolicy` is the single gate. Anything that switches a device goes through it. It refuses non-game controllers, keyboard and mouse composites, and empty node lists.

## Windows device lessons (hard-won)

- **How a controller is toggled:** SetupAPI `DIF_PROPERTYCHANGE` with `DICS_ENABLE` / `DICS_DISABLE`. The `pnputil` commands were only used by hand for diagnosis. Do not shell out to `pnputil` in the app.
- **Toggle target selection:** the toggler acts on the USB-level nodes (`USB\...`, shown as "USB Input Device") and falls back to all of the controller's nodes when there are none (see `ToggleTargets`). Disabling the USB-level node also removes the HID children, which is why the state file below is needed.
- **Verify every scope.** `DICS_FLAG_CONFIGSPECIFIC` can return success without changing anything. Disable tries config-specific then global; enable tries global then config-specific. Re-read the device state after each attempt and only report success if it really changed.
- **`DN_NEED_RESTART` (0x100) blocks all further toggling** of that device until Windows restarts, and shows up as `ERROR_INVALID_DATA` ("The data is invalid") and "Device is pending system reboot". Detect it up front and tell the user, instead of retrying. It was left behind by earlier failed attempts.
- **Vendor drivers can force "restart required" on every disable.** The Logitech/Saitek X-56 driver (oem97 to oem110, `saik2221`/`saika221` HID nodes, installed 2026-11-08) made the first disable set `DN_NEED_RESTART` (status 0x180210A) and blocked re-enabling until a reboot. The same build worked before the driver and after it was removed with `pnputil /delete-driver ... /uninstall /force`, so it was not a code regression. Before suspecting our code, check the log in `%ProgramData%\HotasController\logs\` and the driver install dates (`pnputil /enum-drivers`). With the generic `input.inf` driver, a disabled controller's light goes out; if it stays on, the device is not really stopped. An earlier note that this is normal was wrong.
- **A disabled USB device loses its HID children**, so it can no longer be recognised as a game controller by usage. That is why `disabled-devices.json` exists: controllers the app disabled are recognised from the state file (`RecognisedFromState`). Known gaps: a controller the app disabled is not listed while unplugged, and one disabled outside the app is not listed while disabled.
- **Group nodes per physical device:** climb parents past USB composite-interface nodes (`&MI_xx`) to reach the device. Name from `DEVPKEY_Device_BusReportedDeviceDesc`.
- **Game controller = HID usage page 0x01 with usage 0x04 (Joystick), 0x05 (Game Pad) or 0x08 (Multi-axis).** Mouse (0x02), Keyboard (0x06) and Keypad (0x07) are never listed. Read usage with access 0 so games holding the device are not disturbed.
- **Instance id formats differ by bus:**
  - USB: `USB\VID_054C&PID_0CE6\...` (VID_ and PID_ with underscores).
  - Bluetooth: `BTHENUM\{00001124-...}_VID&0002054C_PID&0CE6\...`. The 8-digit vendor part starts with the id source (`0001` USB-IF, `0002` Bluetooth SIG), then the real vendor id. Bluetooth low energy uses `{00001812-...}`.
  - A wired and a Bluetooth DualSense are **two separate Windows devices**. Never merge them on VID:PID alone. The VID:PID fallback in preset matching only applies within the same bus (`InstanceIdParser.GetBus`).
- **CsWin32 gotchas:** list every API in `NativeMethods.txt` (including structs such as `SP_PROPCHANGE_PARAMS`). The device id function is `CM_Get_Device_IDW`. Signatures often differ from the raw Win32 docs (safe handles, `out` parameters), so read the generated code.
- **Device change events:** `WM_DEVICECHANGE` through an `HwndSource` hook, debounced about 700 ms with a `DispatcherTimer`, because plug and unplug produce bursts of messages.

## WPF lessons

- **Do not use `BasedOn="{StaticResource {x:Type ListViewItem}}"`.** It threw `Cannot find resource named 'System.Windows.Controls.ListViewItem'` at startup. To grey out rows, use an implicit `TextBlock` style with a `DataTrigger` inside `ListView.Resources`.
- **Checkbox that acts immediately:** bind `IsChecked` two-way to a property whose setter starts the async toggle, and raise `PropertyChanged` for it afterwards so the box snaps back if the change failed. A one-way binding is replaced once the user clicks.
- **ComboBox quirk:** clearing the bound `ItemsSource` resets the selected item to `null` and pushes that back to the view model. Read the wanted selection **before** clearing the list.
- **Status colours** (green enabled, red disabled, yellow working, grey unplugged) are `DataTrigger`s on the ellipse; put the "working" trigger last so it wins.
- **Icons** are glyphs from the system icon font (`Segoe MDL2 Assets`): Bluetooth `E702`, USB `E88E`. No image assets.
- GridView columns cannot be star-sized, so keep the default window width close to the sum of the column widths to avoid blank space on the right.

## Shell and tooling lessons (Windows)

- The primary shell is **PowerShell 5.1**: no `&&`, no `?:`. Use `;` and `if ($?)`.
- **Passing text with double quotes to a native command** (for example a PR body to `gh`) breaks arguments in 5.1. Put the text in a variable (`$body = @'...'@`) and pass `--body $body`. Do not use `jq` expressions with backslashes in PowerShell. Use `ConvertFrom-Json`.
- A multi-line commit message via `git commit -F - @'...'@` failed. Use several `-m` arguments with backtick line continuations.
- **Bash heredocs mangle backslashes and some quotes.** A regex lost a `\\` and a large heredoc failed outright. Create files with the Write tool and change them with Edit instead.
- Python is not installed. Use the Edit tool, `sed` or PowerShell for text changes.
- **Windows PowerShell 5.1 cannot load .NET 8 assemblies**, so you cannot call the project's DLLs from a PowerShell script. To try Win32 code, make a throwaway console project in the session scratchpad that references the built DLLs, run it against a temp folder only, then delete the project and the temp folder. The agent shell is usually **not elevated**, so anything that needs administrator rights (accepting a properly owned data folder, toggling devices) can only be verified by the user running the app.
- The data folder is limited to administrators, so `tools/HotasController.Dump` shows a warning instead of the saved state unless it runs in an elevated terminal.
- The `gh` CLI may not be on the PATH of a session that started before it was installed. Refresh `$env:Path` from the Machine and User values, or restart the session.
- Git on Windows ignores case in `.gitignore` patterns. A template `*.app` pattern hid the whole `src/HotasController.App` folder, and the first push lacked it. Keep `.gitignore` small and check with `git status` that new source folders are tracked.
- LF to CRLF warnings on commit are expected on this machine. Ignore them.

## Git and GitHub workflow

- `main` is protected by a ruleset: changes go through a **pull request**; deletion and force-push are blocked. Do not push to `main` directly.
- **Branch names:** `dev/feature/<name>`, `dev/bugfix/<name>`, `chore/<name>`. Create the branch before making changes. One topic per PR.
- **Stage explicitly** (`git add README.md src tests`), never `git add -A`. Stray files such as screenshots in the project root were staged by accident once. Check `git status` before committing.
- Commit messages: short imperative subject, bullet body, then the trailer `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. PR bodies end with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
- PR descriptions: summary, matching or design notes, and a testing section stating exactly what was and was not verified on hardware.
- After the user merges: `git checkout main`, `git pull --ff-only`, then offer to delete the merged local branches.
- `scratchpad/` is a local, gitignored working folder (plans, task notes). Do not commit it. `PLAN.md` lives there now, not in the repo.
- Never rewrite published history without being asked. Old scratchpad files remain in early commits (nothing sensitive).

## Temp files

- Leave nothing behind in `%TEMP%`. Delete scratch builds, throwaway clones and test output as soon as they are not needed.
- Only delete what you can prove you created (for example dotnet logs whose first lines name this project). `%TEMP%` also holds other apps' files, other projects' session folders under `%TEMP%\claude`, and the single-file extraction cache `%TEMP%\.net\HotasController.App`, which the user's app created. Ask before touching those.

## Known limitations (documented in README)

- Controllers the app disabled are not shown while unplugged. Controllers disabled outside the app are not shown while disabled, so a preset shows them as Unplugged.
- A device can be flagged "restart required" by Windows if a program holds it open during disable.
- Disabling and enabling works for both USB controllers and Bluetooth controllers (the Bluetooth DualSense was confirmed by the user on real hardware).
- Linking a wired and a Bluetooth connection of the same physical controller was considered and rejected: it would need device-specific code (reading the Bluetooth address over USB) and guessing by VID:PID would merge two different controllers.
