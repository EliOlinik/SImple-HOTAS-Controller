using HotasController.Core.Presets;

namespace HotasController.Tests;

public sealed class PresetStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotas-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "sub", "presets.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static Preset P(string name, bool enabled = true) =>
        new(name, new[] { new PresetEntry(@"USB\VID_1&PID_2\1", "Stick", 1, 2, enabled) });

    [Fact]
    public void Load_MissingFile_ReturnsEmpty() =>
        Assert.Empty(new PresetStore(FilePath).Load());

    [Fact]
    public void RoundTrip()
    {
        new PresetStore(FilePath).Save(P("Race"));
        var loaded = Assert.Single(new PresetStore(FilePath).Load());

        Assert.Equal("Race", loaded.Name);
        Assert.Equal(P("Race").Entries.Single(), loaded.Entries.Single());
    }

    [Fact]
    public void RoundTrip_NullVidPid()
    {
        var preset = new Preset("X", new[] { new PresetEntry("A", "N", null, null, false) });
        new PresetStore(FilePath).Save(preset);
        var entry = Assert.Single(Assert.Single(new PresetStore(FilePath).Load()).Entries);
        Assert.Null(entry.VendorId);
        Assert.False(entry.Enabled);
    }

    [Fact]
    public void Save_ReplacesSameNameCaseInsensitive()
    {
        var s = new PresetStore(FilePath);
        s.Save(P("Race", enabled: true));
        s.Save(P("RACE", enabled: false));

        var loaded = Assert.Single(s.Load());
        Assert.False(loaded.Entries.Single().Enabled);
    }

    [Fact]
    public void Delete_RemovesOnlyThatPreset()
    {
        var s = new PresetStore(FilePath);
        s.Save(P("A"));
        s.Save(P("B"));
        s.Delete("a");

        Assert.Equal("B", Assert.Single(s.Load()).Name);
    }

    [Fact]
    public void Delete_Absent_IsNoOp()
    {
        var s = new PresetStore(FilePath);
        s.Save(P("A"));
        s.Delete("nope");
        Assert.Single(s.Load());
    }

    [Fact]
    public void CorruptFile_ReturnsEmpty_AndKeepsBackup()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Empty(new PresetStore(FilePath).Load());
        Assert.True(File.Exists(FilePath + ".corrupt"));
    }

    [Fact]
    public void WrongShape_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "[null]");

        Assert.Empty(new PresetStore(FilePath).Load());
    }
}
