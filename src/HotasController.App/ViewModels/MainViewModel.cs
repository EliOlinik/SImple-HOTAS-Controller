using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotasController.App.Services;
using HotasController.Core.Abstractions;
using HotasController.Core.Models;
using HotasController.Core.Presets;
using HotasController.Core.Safety;

namespace HotasController.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDeviceService _devices;
    private readonly ISafetyPolicy _safety;
    private readonly IDeviceToggler _toggler;
    private readonly IConfirmationService _confirm;
    private readonly IPresetStore _presetStore;
    private readonly IPromptService _prompt;

    private IReadOnlyList<GameController> _lastControllers = Array.Empty<GameController>();
    private List<Preset> _presets = new();

    public MainViewModel(IDeviceService devices, ISafetyPolicy safety, IDeviceToggler toggler,
        IConfirmationService confirm, IPresetStore presetStore, IPromptService prompt)
    {
        _devices = devices;
        _safety = safety;
        _toggler = toggler;
        _confirm = confirm;
        _presetStore = presetStore;
        _prompt = prompt;
        LoadPresets(selectName: null);
        Reload();
    }

    /// <summary>Rows: plugged-in controllers, then (for the selected preset) entries that are not plugged in.</summary>
    public ObservableCollection<ControllerViewModel> Controllers { get; } = new();

    public ObservableCollection<string> PresetNames { get; } = new();

    public bool HasControllers => Controllers.Count > 0;

    /// <summary>True when at least one plugged-in controller is not (fully) enabled.</summary>
    public bool HasDisabledControllers => Controllers.Any(c => !c.IsUnplugged && !c.IsEnabled);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReEnableAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreatePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdatePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    /// <summary>Name of the preset chosen in the dropdown; null when none.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyPresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdatePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeletePresetCommand))]
    private string? _selectedPresetName;

    partial void OnSelectedPresetNameChanged(string? value) => RebuildRows();

    private Preset? SelectedPreset =>
        SelectedPresetName is { } n
            ? _presets.FirstOrDefault(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))
            : null;

    private bool CanRefresh() => !IsBusy;
    private bool CanUsePreset() => !IsBusy && SelectedPreset is not null;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private void Refresh()
    {
        Reload();
        var plugged = _lastControllers.Count;
        StatusMessage = plugged > 0 ? $"Found {plugged} controller(s)." : "No game controllers found.";
    }

    private string _lastSignature = "";

    /// <summary>Re-reads controllers from the service and rebuilds the list. Returns true when the list differs from before.</summary>
    public bool Reload()
    {
        _lastControllers = _devices.GetGameControllers();
        var signature = string.Join("|", _lastControllers.Select(c => $"{c.PhysicalDeviceInstanceId}:{c.IsEnabled}:{c.IsFullyDisabled}"));
        var changed = signature != _lastSignature;
        _lastSignature = signature;
        RebuildRows();
        return changed;
    }

    private void RebuildRows()
    {
        Controllers.Clear();
        foreach (var c in _lastControllers)
            Controllers.Add(new ControllerViewModel(c, _safety, _toggler, this));

        if (SelectedPreset is { } preset)
        {
            foreach (var item in PresetPlanner.Plan(preset, _lastControllers).Where(i => i.Kind == PresetPlanKind.Unplugged))
                Controllers.Add(new ControllerViewModel(item.Entry, _safety, _toggler, this));
        }

        OnPropertyChanged(nameof(HasControllers));
        OnPropertyChanged(nameof(HasDisabledControllers));
    }

    /// <summary>Windows reported that devices came or went. Ignored while an operation runs (it reloads itself).</summary>
    public void OnDevicesChanged()
    {
        if (IsBusy) return;
        if (Reload()) StatusMessage = "Device list updated.";
    }

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var c in Controllers) c.OnOwnerBusyChanged();
    }

    // ---- presets -------------------------------------------------------------------------------------------

    private void LoadPresets(string? selectName)
    {
        try
        {
            _presets = _presetStore.Load().ToList();
        }
        catch (Exception ex)
        {
            _presets = new();
            StatusMessage = $"Could not load presets: {ex.Message}";
        }

        // Read the wanted selection first: clearing the list makes the ComboBox reset SelectedPresetName to null.
        var keep = selectName ?? SelectedPresetName;

        PresetNames.Clear();
        foreach (var p in _presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            PresetNames.Add(p.Name);

        SelectedPresetName = keep is null
            ? null
            : PresetNames.FirstOrDefault(n => string.Equals(n, keep, StringComparison.OrdinalIgnoreCase));
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private void CreatePreset()
    {
        var input = _prompt.PromptText("Create preset", "Name for the new preset (it records the current state of every plugged-in controller):");
        if (input is null) return;

        var name = input.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "A preset needs a name.";
            return;
        }

        if (_presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"A preset named \"{name}\" already exists. Select it and use Update to change it.";
            return;
        }

        Reload();
        if (_lastControllers.Count == 0)
        {
            StatusMessage = "No game controllers are plugged in, so there is nothing to save.";
            return;
        }

        try
        {
            var preset = PresetPlanner.Capture(name, _lastControllers);
            _presetStore.Save(preset);
            LoadPresets(selectName: name);
            StatusMessage = $"Created preset \"{name}\" with {preset.Entries.Count} controller(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save the preset: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUsePreset))]
    private void UpdatePreset()
    {
        if (SelectedPreset is not { } existing) return;

        if (!_confirm.Confirm("Update preset",
                $"Overwrite the preset \"{existing.Name}\" with the current state of the plugged-in controllers? The saved states will be replaced."))
        {
            StatusMessage = "Cancelled.";
            return;
        }

        Reload();
        try
        {
            var updated = PresetPlanner.Update(existing, _lastControllers);
            _presetStore.Save(updated);
            LoadPresets(selectName: existing.Name);
            RebuildRows(); // the selection did not change, so refresh the unplugged rows explicitly
            StatusMessage = $"Updated preset \"{existing.Name}\" from the current state ({updated.Entries.Count} controller(s)).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save the preset: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUsePreset))]
    private void DeletePreset()
    {
        if (SelectedPreset is not { } preset) return;

        if (!_confirm.Confirm("Delete preset", $"Delete the preset \"{preset.Name}\"? This cannot be undone."))
        {
            StatusMessage = "Cancelled.";
            return;
        }

        try
        {
            _presetStore.Delete(preset.Name);
            SelectedPresetName = null;
            LoadPresets(selectName: null);
            StatusMessage = $"Deleted preset \"{preset.Name}\".";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not delete the preset: {ex.Message}";
        }
    }

    /// <summary>Switches every plugged-in controller in the preset to its saved state. Controllers not in the preset are left alone.</summary>
    [RelayCommand(CanExecute = nameof(CanUsePreset))]
    private async Task ApplyPresetAsync()
    {
        if (SelectedPreset is not { } preset) return;

        Reload();
        var plan = PresetPlanner.Plan(preset, _lastControllers);
        var work = plan.Where(i => i.Kind is PresetPlanKind.Enable or PresetPlanKind.Disable)
            .OrderBy(i => i.Kind == PresetPlanKind.Enable ? 0 : 1) // enable first: if something fails, fewer devices are left off
            .ToList();
        var correct = plan.Count(i => i.Kind == PresetPlanKind.AlreadyCorrect);
        var unplugged = plan.Count(i => i.Kind == PresetPlanKind.Unplugged);

        if (work.Count == 0)
        {
            StatusMessage = $"Preset \"{preset.Name}\": nothing to change ({correct} already correct, {unplugged} unplugged).";
            return;
        }

        IsBusy = true;
        var rows = work.ToDictionary(
            i => i.Controller!.PhysicalDeviceInstanceId,
            i => Controllers.FirstOrDefault(r => r.Controller?.PhysicalDeviceInstanceId == i.Controller!.PhysicalDeviceInstanceId),
            StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Values) if (row is not null) row.WorkingText = "Waiting...";

        try
        {
            var changed = 0;
            var failures = new List<string>();
            foreach (var item in work)
            {
                var controller = item.Controller!;
                var enable = item.Kind == PresetPlanKind.Enable;
                rows.TryGetValue(controller.PhysicalDeviceInstanceId, out var row);

                var decision = _safety.Evaluate(controller, enable ? DeviceAction.Enable : DeviceAction.Disable);
                if (!decision.Allowed)
                {
                    failures.Add($"{controller.Name}: {decision.Reason}");
                    if (row is not null) row.WorkingText = null;
                    continue;
                }

                if (row is not null) row.WorkingText = enable ? "Enabling..." : "Disabling...";
                StatusMessage = $"Applying \"{preset.Name}\": {(enable ? "enabling" : "disabling")} {controller.Name} ({changed + failures.Count + 1} of {work.Count})...";
                try
                {
                    var result = await _toggler.SetEnabledAsync(controller, enable);
                    if (result.Succeeded)
                    {
                        changed++;
                        if (row is not null) row.ResultEnabled = enable;
                    }
                    else
                    {
                        failures.Add($"{controller.Name}: {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{controller.Name}: {ex.Message}");
                }
                finally
                {
                    if (row is not null) row.WorkingText = null;
                }
            }

            StatusMessage = $"Applied \"{preset.Name}\": {changed} changed, {correct} already correct, {unplugged} unplugged" +
                            (failures.Count == 0 ? "." : $", {failures.Count} failed: {string.Join(" | ", failures)}");
        }
        finally
        {
            IsBusy = false;
        }
        Reload();
    }

    /// <summary>Enables every listed controller that is not fully enabled (for example ones this app disabled).</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task ReEnableAllAsync()
    {
        var targets = Controllers.Where(c => !c.IsUnplugged && !c.IsEnabled).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "Nothing to re-enable.";
            return;
        }

        IsBusy = true;
        foreach (var t in targets) t.WorkingText = "Waiting...";
        try
        {
            var enabled = 0;
            var failures = new List<string>();
            foreach (var row in targets)
            {
                var controller = row.Controller!;
                var decision = _safety.Evaluate(controller, DeviceAction.Enable);
                if (!decision.Allowed)
                {
                    failures.Add($"{controller.Name}: {decision.Reason}");
                    row.WorkingText = null;
                    continue;
                }

                row.WorkingText = "Enabling...";
                StatusMessage = $"Enabling {controller.Name} ({enabled + failures.Count + 1} of {targets.Count})...";
                try
                {
                    var result = await _toggler.SetEnabledAsync(controller, true);
                    if (result.Succeeded)
                    {
                        enabled++;
                        row.ResultEnabled = true;
                    }
                    else
                    {
                        failures.Add($"{controller.Name}: {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{controller.Name}: {ex.Message}");
                }
                finally
                {
                    row.WorkingText = null;
                }
            }

            StatusMessage = failures.Count == 0
                ? $"Re-enabled {enabled} controller(s)."
                : $"Re-enabled {enabled}, {failures.Count} failed: {string.Join(" | ", failures)}";
        }
        finally
        {
            IsBusy = false;
        }
        Reload();
    }
}
