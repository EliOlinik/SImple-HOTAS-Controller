using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotasController.Core;
using HotasController.Core.Abstractions;
using HotasController.Core.Models;
using HotasController.Core.Presets;
using HotasController.Core.Safety;

namespace HotasController.App.ViewModels;

/// <summary>
/// One row of the list: either a plugged-in controller, or (when a preset is selected) a preset entry
/// whose controller is not plugged in.
/// </summary>
public partial class ControllerViewModel : ObservableObject
{
    private readonly ISafetyPolicy _safety;
    private readonly IDeviceToggler _toggler;
    private readonly MainViewModel _owner;
    private readonly PresetEntry? _unpluggedEntry;

    /// <summary>A row for a plugged-in controller.</summary>
    public ControllerViewModel(GameController controller, ISafetyPolicy safety, IDeviceToggler toggler, MainViewModel owner)
    {
        Controller = controller;
        _safety = safety;
        _toggler = toggler;
        _owner = owner;
    }

    /// <summary>A row for a preset entry with no matching plugged-in controller.</summary>
    public ControllerViewModel(PresetEntry unpluggedEntry, ISafetyPolicy safety, IDeviceToggler toggler, MainViewModel owner)
    {
        _unpluggedEntry = unpluggedEntry;
        _safety = safety;
        _toggler = toggler;
        _owner = owner;
    }

    /// <summary>The plugged-in controller; null for an unplugged row.</summary>
    public GameController? Controller { get; }

    /// <summary>What Apply would do to this row for the selected preset; empty when no saved preset is selected.</summary>
    public string PlannedText { get; init; } = "";

    /// <summary>True when Apply would switch this controller (the row is shown in bold).</summary>
    public bool IsPlannedChange { get; init; }

    public bool IsUnplugged => Controller is null;

    /// <summary>True for controllers connected over Bluetooth (also for an unplugged preset entry that was one).</summary>
    public bool IsBluetooth => InstanceIdParser.IsBluetooth(DeviceId);

    /// <summary>True for controllers connected over USB (also for an unplugged preset entry that was one).</summary>
    public bool IsUsb => InstanceIdParser.IsUsb(DeviceId);

    private string DeviceId => Controller?.PhysicalDeviceInstanceId ?? _unpluggedEntry!.PhysicalDeviceInstanceId;
    public string Name => Controller?.Name ?? _unpluggedEntry!.Name;

    public string VidPid
    {
        get
        {
            var (v, p) = Controller is { } c ? (c.VendorId, c.ProductId) : (_unpluggedEntry!.VendorId, _unpluggedEntry.ProductId);
            return v is { } vv && p is { } pp ? $"{vv:X4}:{pp:X4}" : "-";
        }
    }

    public string NodeCountText => Controller is { } c ? c.Nodes.Count.ToString() : "-";

    /// <summary>Text shown (with a yellow indicator) while an operation on this controller is queued or running; null when idle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking), nameof(StatusText))]
    private string? _workingText;

    /// <summary>Outcome of the last successful operation, shown until the list is reloaded from the OS.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled), nameof(IsChecked), nameof(StatusText))]
    private bool? _resultEnabled;

    public bool IsWorking => WorkingText is not null;

    /// <summary>True when plugged in and fully enabled. Always false for an unplugged row.</summary>
    public bool IsEnabled => Controller is not null && (ResultEnabled ?? Controller.IsEnabled);

    public string StatusText =>
        WorkingText ?? (Controller is null
            ? "Unplugged"
            : ResultEnabled is { } r
                ? (r ? "Enabled" : "Disabled")
                : Controller.IsEnabled ? "Enabled" : Controller.IsFullyDisabled ? "Disabled" : "Partially disabled");

    /// <summary>The checkbox is only usable for plugged-in controllers while no other operation runs.</summary>
    public bool CanInteract => !IsUnplugged && !_owner.IsBusy;

    /// <summary>
    /// Checkbox state: checked means enabled. Changing it switches the real device immediately; the box snaps back
    /// to the real state if the change fails.
    /// </summary>
    public bool IsChecked
    {
        get => IsEnabled;
        set
        {
            if (value == IsEnabled || !CanInteract) return;
            _ = SetEnabledAsync(value);
        }
    }

    /// <summary>Called by the owner when its busy state changes so the checkbox and Forget button grey out.</summary>
    public void OnOwnerBusyChanged()
    {
        OnPropertyChanged(nameof(CanInteract));
        ForgetCommand.NotifyCanExecuteChanged();
    }

    private bool CanForget() => _unpluggedEntry is not null && !_owner.IsBusy;

    /// <summary>For an unplugged row: removes this controller from the selected preset.</summary>
    [RelayCommand(CanExecute = nameof(CanForget))]
    private void Forget() => _owner.ForgetEntry(_unpluggedEntry!);

    private async Task SetEnabledAsync(bool enable)
    {
        var controller = Controller!;
        var action = enable ? DeviceAction.Enable : DeviceAction.Disable;
        var verb = enable ? "enable" : "disable";

        var decision = _safety.Evaluate(controller, action);
        if (!decision.Allowed)
        {
            _owner.StatusMessage = decision.Reason ?? $"Cannot {verb} {Name}.";
            OnPropertyChanged(nameof(IsChecked));
            return;
        }

        _owner.IsBusy = true;
        WorkingText = enable ? "Enabling..." : "Disabling...";
        try
        {
            _owner.StatusMessage = $"{(enable ? "Enabling" : "Disabling")} {Name}...";
            var result = await _toggler.SetEnabledAsync(controller, enable);
            if (result.Succeeded) ResultEnabled = enable;
            _owner.StatusMessage = result.Message
                ?? (result.Succeeded ? $"{Name}: {verb}d." : $"Failed to {verb} {Name}.");
        }
        catch (Exception ex)
        {
            _owner.StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            WorkingText = null;
            _owner.IsBusy = false;
            OnPropertyChanged(nameof(IsChecked));
        }
        _owner.Reload();
    }
}
