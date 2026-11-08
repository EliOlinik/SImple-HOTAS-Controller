# HOTAS Controller

A small Windows utility to quickly enable and disable the game controllers plugged into your machine.

## Motivation

I have twitchy pots on some of my controllers, which causes several issues while they are plugged in. The main one is that the HOTAS steals focus from my racing controller. It also prevents my machine from sleeping for long periods of time. To resolve this I had to either disable the controllers in Device Manager or, more often, just unplug them while not in use. I decided to create this small utility so I can do it without getting up from my chair.

## Usage

- The window lists every game controller currently plugged in (joysticks, gamepads, HOTAS sticks and throttles).
- Untick the checkbox next to a controller to switch it off, and tick it to switch it back on. The change happens immediately.
- **Re-enable all** turns back on every listed controller that is not fully enabled, including ones this app disabled earlier.
- The status dot shows each controller's state: green is enabled, red is disabled, yellow means the app is working on it, and grey means unplugged.
- A small icon next to a controller's name shows how it is connected: a USB symbol for USB, or a blue Bluetooth symbol for Bluetooth.
- The list refreshes automatically when you plug in or unplug a controller.

### Presets

A preset is a named set of controller states, for example "Racing" with the HOTAS off and the wheel on.

- The dropdown always starts with **&lt;Default&gt;**, which is selected when the app starts. It is a built-in, read-only placeholder that shows the live state of your controllers. It cannot be applied, updated, deleted or used as a preset name, and it is not stored in `presets.json`. Deleting the selected preset switches back to it.
- **Create** asks for a name and saves the current state of every plugged-in controller under it. Names must be unique, and at least one controller must be plugged in.
- Pick a preset in the dropdown and press **Apply** to switch the controllers to the saved states. Controllers that are not in the preset are left alone, and the status bar reports how many were changed, already correct, unplugged or failed. Selecting a preset only previews it; nothing changes until you press **Apply**. The preview is a summary line below the preset controls (for example "Apply will: enable 1, disable 2 · 1 already correct · 1 unplugged") and an **After apply** column that says what will happen to each row: **→ Enable** or **→ Disable** (shown in bold), No change, Not in preset, or Skipped (unplugged). When there is nothing to change, **Apply** is greyed out.
- **Update** overwrites the selected preset with the current states, after asking you to confirm. It also adds controllers that are new and keeps the entries for controllers that are unplugged.
- **Delete** removes the selected preset, after asking you to confirm.
- When a preset is selected, controllers it contains that are not plugged in appear greyed out as **Unplugged**, with a disabled checkbox. They are skipped when you apply the preset. Press **Forget** on such a row to remove that controller from the selected preset (after confirmation), for example when you no longer own it. Other presets are not affected.
- A controller is matched to a preset entry by its device ID. If you move it to another USB port, it is matched by VID:PID instead, as long as that is unambiguous (two identical sticks are never mixed up) and the connection type is the same. A controller connected by cable and the same model connected over Bluetooth are separate Windows devices, so each gets its own row and its own preset entry.

## Requirements

- Windows 10 or 11, x64.
- Administrator rights. The app asks for elevation (UAC prompt) on start, because enabling and disabling devices requires it.
- To build from source: the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Build and run

```
dotnet build HotasController.sln
dotnet test HotasController.sln
```

Because the app requires administrator rights, `dotnet run --project src/HotasController.App` must be started from an elevated terminal. Adding `-- --fake` runs the UI against fake devices, which is useful for trying it out without touching real hardware. In this mode presets are kept in memory only and nothing is written to disk.

To produce a single self-contained executable (no .NET install needed, around 65 MB), run:

```
publish.cmd
```

The result is written to the `dist` folder.

## How it works

The app performs two simple tasks:

1. It scans the HID devices on the machine and keeps only game controllers (joystick, gamepad and multi-axis usages). Keyboards and mice are never listed.
2. It enables or disables the selected controller through the Windows device-installation API (SetupAPI), the same mechanism Device Manager uses for its Enable/Disable device command. No drivers are changed or removed.

Presets are just saved lists of controllers and their wanted states. Applying one runs the same enable or disable step, with the same safety checks, for each controller in turn.

The app checks the result after every change and reports a failure instead of assuming success. Controllers that the app disabled are remembered so they can be re-enabled later, even though Windows hides a disabled device.

## Limitations

- A controller that the app disabled does not appear in the list while it is unplugged.
- A controller disabled outside the app (for example in Device Manager) is not shown while it is disabled. A preset therefore shows it as **Unplugged** and cannot enable it.
- If another program holds a controller open when it is disabled, Windows may mark the device as "restart required". The app reports this and refuses further changes to that device until Windows is restarted.

## Files the app creates

Everything is stored in `%ProgramData%\HotasController\`:

- `disabled-devices.json`: the controllers this app has disabled.
- `presets.json`: your saved presets.
- `logs\`: daily log files, with the last 14 days kept.

No data leaves your machine.

## License

[MIT](LICENSE)
