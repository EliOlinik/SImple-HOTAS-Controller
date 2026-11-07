using HotasController.Core.State;

namespace HotasController.Tests;

public sealed class DisabledStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotas-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "sub", "disabled.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static DisabledRecord Rec(string id, string name = "Stick") =>
        new(id, name, 0x044F, 0xB10A, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    [Fact]
    public void RoundTrip()
    {
        var r = Rec(@"USB\VID_044F&PID_B10A\1");
        new DisabledStateStore(FilePath).Add(r);
        var loaded = new DisabledStateStore(FilePath).Load();
        Assert.Equal(new[] { r }, loaded);
    }

    [Fact]
    public void RoundTrip_NullVidPid()
    {
        var r = new DisabledRecord("A", "X", null, null, DateTimeOffset.UtcNow);
        var s = new DisabledStateStore(FilePath);
        s.Add(r);
        Assert.Equal(r, Assert.Single(s.Load()));
    }

    [Fact]
    public void Add_ReplacesDuplicateCaseInsensitive()
    {
        var s = new DisabledStateStore(FilePath);
        s.Add(Rec("USB_ABC", "Old"));
        s.Add(Rec("usb_abc", "New"));
        var only = Assert.Single(s.Load());
        Assert.Equal("New", only.Name);
    }

    [Fact]
    public void Remove_RemovesAndIsNoOpWhenAbsent()
    {
        var s = new DisabledStateStore(FilePath);
        s.Remove("missing");
        s.Add(Rec("A"));
        s.Add(Rec("B"));
        s.Remove("a");
        s.Remove("zzz");
        Assert.Equal("B", Assert.Single(s.Load()).PhysicalDeviceInstanceId);
    }

    [Fact]
    public void Clear_EmptiesStore()
    {
        var s = new DisabledStateStore(FilePath);
        s.Add(Rec("A"));
        s.Clear();
        Assert.Empty(s.Load());
    }

    [Fact]
    public void MissingFile_ReturnsEmpty() => Assert.Empty(new DisabledStateStore(FilePath).Load());

    [Fact]
    public void EmptyFile_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "");
        Assert.Empty(new DisabledStateStore(FilePath).Load());
    }

    [Fact]
    public void CorruptFile_ReturnsEmpty_AndKeepsCopy()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json [");
        var s = new DisabledStateStore(FilePath);
        Assert.Empty(s.Load());
        Assert.True(File.Exists(FilePath + ".corrupt"));
        s.Add(Rec("A"));
        Assert.Single(s.Load());
    }

    [Fact]
    public void Directory_AutoCreated()
    {
        Assert.False(Directory.Exists(_dir));
        new DisabledStateStore(FilePath).Add(Rec("A"));
        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void ConcurrentAdds_DoNotLoseRecords()
    {
        var s = new DisabledStateStore(FilePath);
        Parallel.For(0, 40, i => s.Add(Rec("dev" + i)));
        Assert.Equal(40, s.Load().Select(r => r.PhysicalDeviceInstanceId).Distinct().Count());
    }

    [Fact]
    public void DefaultPath_IsUnderProgramData()
    {
        Assert.EndsWith(Path.Combine("HotasController", "disabled-devices.json"), DisabledStateStore.DefaultPath);
    }
}
