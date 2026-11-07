using System.IO;
using System.Windows;
using System.Windows.Threading;
using HotasController.App.Fakes;
using HotasController.App.Services;
using HotasController.App.ViewModels;
using HotasController.Core.Safety;
using HotasController.Core.State;
using HotasController.Win32;
using Serilog;

namespace HotasController.App;

public partial class App : Application
{
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HotasController", "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ConfigureLogging();

        var useFakes = e.Args.Contains("--fake", StringComparer.OrdinalIgnoreCase);
        Log.Information("Started (pid {Pid}, fakes={Fakes})", Environment.ProcessId, useFakes);

        MainViewModel viewModel;
        try
        {
            viewModel = CreateMainViewModel(useFakes);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Could not start");
            ShowError("The app could not start.", ex);
            Shutdown(1);
            return;
        }
        new MainWindow(viewModel).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Exiting");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void ConfigureLogging()
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(LogDirectory, "hotas-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch
        {
            // Logging is best effort; the app still works without it.
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", args.IsTerminating);
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        ShowError("Something went wrong. The app is still running.", e.Exception);
        e.Handled = true;
    }

    private static void ShowError(string headline, Exception ex) =>
        MessageBox.Show($"{headline}\n\n{ex.Message}\n\nDetails were written to:\n{LogDirectory}",
            "HOTAS Controller", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>
    /// COMPOSITION ROOT. Real Win32 services by default; <c>--fake</c> runs on in-memory fakes so the UI
    /// can be tried without touching any hardware.
    /// </summary>
    public static MainViewModel CreateMainViewModel(bool useFakes = false)
    {
        var confirm = new MessageBoxConfirmationService();

        if (useFakes)
        {
            var fakeDevices = new FakeDeviceService();
            return new MainViewModel(fakeDevices, new FakeSafetyPolicy(), new FakeDeviceToggler(fakeDevices), confirm);
        }

        // One shared store instance: its lock only protects a single instance per file.
        var store = new DisabledStateStore(DisabledStateStore.DefaultPath);
        var devices = new DeviceService(store);
        var safety = new DeviceSafetyPolicy();
        return new MainViewModel(devices, safety, new DeviceToggler(devices, safety, store), confirm);
    }
}
