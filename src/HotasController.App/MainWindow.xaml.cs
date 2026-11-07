using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using HotasController.App.ViewModels;

namespace HotasController.App;

public partial class MainWindow : Window
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDevNodesChanged = 0x0007;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(700) };

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        // Plug/unplug produces a burst of messages; reload once after it settles.
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _viewModel.OnDevicesChanged();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange && wParam.ToInt64() is DbtDevNodesChanged or DbtDeviceArrival or DbtDeviceRemoveComplete)
        {
            _debounce.Stop();
            _debounce.Start();
        }
        return IntPtr.Zero;
    }
}
