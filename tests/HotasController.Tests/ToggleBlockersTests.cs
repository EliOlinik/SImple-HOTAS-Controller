using HotasController.Core;
using HotasController.Core.Models;
using Xunit;

namespace HotasController.Tests;

public class ToggleBlockersTests
{
    [Fact]
    public void Relevant_DropsShellNoiseAndDuplicatePids()
    {
        var raw = new[]
        {
            new ToggleBlocker(1, @"C:\Windows\System32\dwm.exe"),
            new ToggleBlocker(2, @"C:\Windows\explorer.exe"),
            new ToggleBlocker(3, @"C:\Program Files\Vendor\Svc.exe", "VendorSvc"),
            new ToggleBlocker(3, @"C:\Program Files\Vendor\Svc.exe", "VendorSvc"),
        };
        var relevant = ToggleBlockers.Relevant(raw);
        Assert.Single(relevant);
        Assert.Equal("Svc.exe", relevant[0].ProcessName);
    }

    [Theory]
    [InlineData(@"C:\Program Files\Vendor\Svc.exe", true)]
    [InlineData(@"C:\Windows\System32\svchost.exe", false)]
    [InlineData(@"c:\windows\system32\svchost.exe", false)]
    [InlineData(@"C:\WindowsApps\Thing\x.exe", true)]
    [InlineData("", false)]
    public void IsStoppableImage_RefusesAnythingUnderTheWindowsFolder(string path, bool expected) =>
        Assert.Equal(expected, ToggleBlockers.IsStoppableImage(path, @"C:\Windows"));

    [Fact]
    public void ToDosPath_ConvertsNtVolumePaths_AndLeavesOthersAlone()
    {
        var map = new Dictionary<string, string> { [@"\Device\HarddiskVolume3"] = "C:", [@"\Device\HarddiskVolume30"] = "D:" };
        Assert.Equal(@"C:\Windows\System32\rundll32.exe", ToggleBlockers.ToDosPath(@"\Device\HarddiskVolume3\Windows\System32\rundll32.exe", map));
        Assert.Equal(@"D:\x.exe", ToggleBlockers.ToDosPath(@"\Device\HarddiskVolume30\x.exe", map));
        Assert.Equal(@"\Device\Unknown\x.exe", ToggleBlockers.ToDosPath(@"\Device\Unknown\x.exe", map));
    }

    [Fact]
    public void IsStoppableImage_RefusesUnconvertedNtPaths() =>
        Assert.False(ToggleBlockers.IsStoppableImage(@"\Device\HarddiskVolume3\Windows\System32\svchost.exe", @"C:\Windows"));

    [Fact]
    public void StoppableServices_ListsEachServiceOnce()
    {
        var list = ToggleBlockers.StoppableServices([
            new ToggleBlocker(1, "a.exe", "SvcA"), new ToggleBlocker(2, "a.exe", "svca"), new ToggleBlocker(3, "b.exe")]);
        Assert.Equal(["SvcA"], list);
    }

    [Fact]
    public void Describe_NamesProcessAndPid_AndIsEmptyWithoutBlockers()
    {
        Assert.Equal("", ToggleBlockers.Describe([]));
        Assert.Contains("Svc.exe (pid 42)", ToggleBlockers.Describe([new ToggleBlocker(42, @"C:\x\Svc.exe")]));
    }
}
