using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotasController.App.Services;
using HotasController.Core.Abstractions;
using HotasController.Core.Models;
using HotasController.Core.Safety;

namespace HotasController.App.ViewModels;

public partial class ControllerViewModel : ObservableObject
{
    private readonly ISafetyPolicy _safety;
    private readonly IDeviceToggler _toggler;
    private readonly IConfirmationService _confirm;
    private readonly MainViewModel _owner;

    public ControllerViewModel(GameController controller, ISafetyPolicy safety, IDeviceToggler toggler,
        IConfirmationService confirm, MainViewModel owner)
    {
        Controller = controller;
        _safety = safety;
        _toggler = toggler;
        _confirm = confirm;
        _owner = owner;
    }

    public GameController Controller { get; }
    public string Name => Controller.Name;
    public string VidPid => Controller.VendorId is { } v && Controller.ProductId is { } p ? $"{v:X4}:{p:X4}" : "-";
    public int NodeCount => Controller.Nodes.Count;

    /// <summary>Text shown (with a yellow indicator) while an operation on this controller is queued or running; null when idle.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking), nameof(StatusText))]
    private string? _workingText;

    /// <summary>Outcome of the last successful operation, shown until the list is reloaded from the OS.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnabled), nameof(StatusText), nameof(ToggleButtonText))]
    private bool? _resultEnabled;

    public bool IsWorking => WorkingText is not null;
    public bool IsEnabled => ResultEnabled ?? Controller.IsEnabled;

    public string StatusText =>
        WorkingText ?? (ResultEnabled is { } r
            ? (r ? "Enabled" : "Disabled")
            : Controller.IsEnabled ? "Enabled" : Controller.IsFullyDisabled ? "Disabled" : "Partially disabled");

    public string ToggleButtonText => IsEnabled ? "Disable" : "Enable";

    /// <summary>Called by the owner when its busy state changes so the row buttons grey out.</summary>
    public void OnOwnerBusyChanged() => ToggleCommand.NotifyCanExecuteChanged();

    private bool CanToggle() => !_owner.IsBusy;

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        if (_owner.IsBusy) return;
        var action = IsEnabled ? DeviceAction.Disable : DeviceAction.Enable;
        var verb = action.ToString().ToLowerInvariant();

        var decision = _safety.Evaluate(Controller, action);
        if (!decision.Allowed)
        {
            _owner.StatusMessage = decision.Reason ?? $"Cannot {verb} {Name}.";
            return;
        }

        if (action == DeviceAction.Disable &&
            !_confirm.Confirm("Disable controller", $"Disable \"{Name}\"? You can enable it again from this app."))
        {
            _owner.StatusMessage = "Cancelled.";
            return;
        }

        _owner.IsBusy = true;
        WorkingText = action == DeviceAction.Disable ? "Disabling..." : "Enabling...";
        try
        {
            _owner.StatusMessage = $"{(action == DeviceAction.Disable ? "Disabling" : "Enabling")} {Name}...";
            var result = await _toggler.SetEnabledAsync(Controller, action == DeviceAction.Enable);
            if (result.Succeeded) ResultEnabled = action == DeviceAction.Enable;
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
        }
        _owner.Reload();
    }
}
