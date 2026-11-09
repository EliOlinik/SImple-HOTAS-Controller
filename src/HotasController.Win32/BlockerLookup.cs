using System.Diagnostics.Eventing.Reader;
using System.ServiceProcess;
using HotasController.Core;
using HotasController.Core.Models;
using Microsoft.Win32;
using Serilog;

namespace HotasController.Win32;

/// <summary>
/// Finds which processes made Windows refuse to stop a device, and which of them are services the app may stop.
/// Windows logs this itself: Kernel-PnP event 225, "The application X with process id N stopped the removal or
/// ejection for the device Y". Reading the System log needs no handle walking and changes nothing.
/// </summary>
public static class BlockerLookup
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Processes that vetoed a stop of one of <paramref name="deviceInstanceIds"/> since <paramref name="since"/>.</summary>
    public static IReadOnlyList<ToggleBlocker> Find(IReadOnlyCollection<string> deviceInstanceIds, DateTime since)
    {
        var found = new List<ToggleBlocker>();
        try
        {
            var from = since.ToUniversalTime().AddSeconds(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[Provider[@Name='Microsoft-Windows-Kernel-PnP'] and EventID=225 and TimeCreated[@SystemTime>='{from}']]]");
            using var reader = new EventLogReader(query);
            for (var e = reader.ReadEvent(); e is not null; e = reader.ReadEvent())
            {
                using (e)
                {
                    // Properties: [0] process id, [2] image path, [4] device instance id.
                    if (e.Properties.Count < 5) continue;
                    var device = e.Properties[4].Value as string;
                    if (device is null || !deviceInstanceIds.Contains(device, StringComparer.OrdinalIgnoreCase)) continue;
                    var image = e.Properties[2].Value as string ?? "";
                    if (image.Length == 0) continue;
                    found.Add(new ToggleBlocker(Convert.ToInt32(e.Properties[0].Value), image));
                }
            }
        }
        catch (Exception ex)
        {
            // Reading the log is a nicety; a failure must never hide the real toggle failure.
            Log.Warning(ex, "Could not read the Kernel-PnP veto events");
            return [];
        }

        return ToggleBlockers.Relevant(found).Select(b => b with { ServiceName = FindServiceName(b.ImagePath) }).ToList();
    }

    /// <summary>The service whose executable is <paramref name="imagePath"/>, if it is a third-party own-process service.</summary>
    private static string? FindServiceName(string imagePath)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!ToggleBlockers.IsStoppableImage(imagePath, windows)) return null;
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null) return null;
            foreach (var name in services.GetSubKeyNames())
            {
                using var key = services.OpenSubKey(name);
                if (key?.GetValue("Type") is not int type || (type & 0x10) == 0) continue; // own-process win32 service
                if (key.GetValue("ImagePath") is not string raw) continue;
                if (string.Equals(ExecutableOf(raw), imagePath, StringComparison.OrdinalIgnoreCase)) return name;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not map {Image} to a service", imagePath);
        }
        return null;
    }

    private static string ExecutableOf(string imagePath)
    {
        var s = Environment.ExpandEnvironmentVariables(imagePath.Trim());
        if (s.StartsWith('"')) { var end = s.IndexOf('"', 1); return end > 0 ? s[1..end] : s.Trim('"'); }
        var exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? s[..(exe + 4)] : s;
    }

    /// <summary>Stops the named services; returns those that really were running and are now stopped (to start again later).</summary>
    public static IReadOnlyList<string> StopServices(IEnumerable<string> names)
    {
        var stopped = new List<string>();
        foreach (var name in names)
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status != ServiceControllerStatus.Running) continue;
                if (!sc.CanStop || sc.DependentServices.Any(d => d.Status != ServiceControllerStatus.Stopped))
                {
                    Log.Warning("Service {Name} cannot be stopped safely (CanStop={CanStop}); leaving it alone", name, sc.CanStop);
                    continue;
                }
                Log.Information("Stopping service {Name} for the toggle", name);
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, ServiceTimeout);
                stopped.Add(name);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not stop service {Name}", name);
            }
        }
        return stopped;
    }

    /// <summary>Starts the services again; returns the ones that did not come back.</summary>
    public static IReadOnlyList<string> StartServices(IEnumerable<string> names)
    {
        var failed = new List<string>();
        foreach (var name in names)
        {
            try
            {
                using var sc = new ServiceController(name);
                Log.Information("Starting service {Name} again", name);
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, ServiceTimeout);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not start service {Name} again", name);
                failed.Add(name);
            }
        }
        return failed;
    }
}
