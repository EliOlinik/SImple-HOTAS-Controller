using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HotasController.App.Services;
using HotasController.Core.Abstractions;
using HotasController.Core.Models;
using HotasController.Core.Safety;

namespace HotasController.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IDeviceService _devices;
    private readonly ISafetyPolicy _safety;
    private readonly IDeviceToggler _toggler;
    private readonly IConfirmationService _confirm;

    public MainViewModel(IDeviceService devices, ISafetyPolicy safety, IDeviceToggler toggler, IConfirmationService confirm)
    {
        _devices = devices;
        _safety = safety;
        _toggler = toggler;
        _confirm = confirm;
        Reload();
    }

    public ObservableCollection<ControllerViewModel> Controllers { get; } = new();

    public bool HasControllers => Controllers.Count > 0;

    /// <summary>True when at least one listed controller is not (fully) enabled.</summary>
    public bool HasDisabledControllers => Controllers.Any(c => !c.IsEnabled);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReEnableAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private void Refresh()
    {
        Reload();
        StatusMessage = HasControllers ? $"Found {Controllers.Count} controller(s)." : "No game controllers found.";
    }

    private bool CanRefresh() => !IsBusy;

    private string _lastSignature = "";

    /// <summary>Re-reads controllers from the service and rebuilds the list. Returns true when the list differs from before.</summary>
    public bool Reload()
    {
        var controllers = _devices.GetGameControllers();
        var signature = string.Join("|", controllers.Select(c => $"{c.PhysicalDeviceInstanceId}:{c.IsEnabled}:{c.IsFullyDisabled}"));
        var changed = signature != _lastSignature;
        _lastSignature = signature;

        Controllers.Clear();
        foreach (var c in controllers)
            Controllers.Add(new ControllerViewModel(c, _safety, _toggler, _confirm, this));
        OnPropertyChanged(nameof(HasControllers));
        OnPropertyChanged(nameof(HasDisabledControllers));
        return changed;
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

    /// <summary>Enables every listed controller that is not fully enabled (for example ones this app disabled).</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task ReEnableAllAsync()
    {
        var targets = Controllers.Where(c => !c.IsEnabled).ToList();
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
                var controller = row.Controller;
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
