using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HotasController.Core.Presets;
using HotasController.Core.State;
using HotasController.Core.Storage;

namespace HotasController.Tests;

/// <summary>How both JSON stores behave with damaged, oversized, over-limit and tampered files.</summary>
public sealed class DataFileHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotas-harden-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(_dir, "disabled.json");
    private string PresetPath => Path.Combine(_dir, "presets.json");

    public DataFileHardeningTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            // A junction must be removed on its own so deleting the folder never follows it.
            foreach (var d in Directory.EnumerateDirectories(_dir))
                if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(d);
            Directory.Delete(_dir, recursive: true);
        }
        catch { /* best effort */ }
    }

    private static DisabledRecord Rec(string id, DateTimeOffset? at = null, string name = "Pad") =>
        new(id, name, 1, 2, at ?? DateTimeOffset.UnixEpoch);

    private static Preset P(string name) => new(name, new[] { new PresetEntry(@"USB\A", "Stick", 1, 2, true) });

    private void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value));

    private string MakeJunction(string name)
    {
        var target = Path.Combine(_dir, name + "-target");
        var link = Path.Combine(_dir, name);
        Directory.CreateDirectory(target);
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, "could not create a junction for the test");
        return target;
    }

    // ---- size ----

    [Fact]
    public void State_FileOverSizeLimit_IsIgnoredAndReported()
    {
        File.WriteAllBytes(StatePath, new byte[DataFileLimits.MaxFileBytes + 1]);
        var s = new DisabledStateStore(StatePath);

        Assert.Empty(s.Load());
        Assert.Contains("larger than", s.LoadProblem);
        Assert.False(File.Exists(StatePath + ".corrupt")); // a huge file is not copied
    }

    [Fact]
    public void Presets_FileOverSizeLimit_IsIgnoredAndReported()
    {
        File.WriteAllText(PresetPath, new string(' ', DataFileLimits.MaxFileBytes + 10));
        var s = new PresetStore(PresetPath);

        Assert.Empty(s.Load());
        Assert.Contains("larger than", s.LoadProblem);
    }

    // ---- limits on content ----

    [Fact]
    public void State_TooManyRecords_FileIsIgnored_AndCopyKept()
    {
        WriteJson(StatePath, Enumerable.Range(0, DataFileLimits.MaxDevices + 1).Select(i => Rec(@"USB\" + i)).ToList());
        var s = new DisabledStateStore(StatePath);

        Assert.Empty(s.Load());
        Assert.Contains("limit is 1000", s.LoadProblem);
        Assert.True(File.Exists(StatePath + ".corrupt"));
    }

    [Fact]
    public void Presets_TooManyPresets_FileIsIgnored_AndCopyKept()
    {
        WriteJson(PresetPath, Enumerable.Range(0, DataFileLimits.MaxPresets + 1).Select(i => P("P" + i)).ToList());
        var s = new PresetStore(PresetPath);

        Assert.Empty(s.Load());
        Assert.Contains("limit is 100", s.LoadProblem);
        Assert.True(File.Exists(PresetPath + ".corrupt"));
    }

    [Fact]
    public void Presets_TooManyDevicesInAPreset_FileIsIgnored()
    {
        var entries = Enumerable.Range(0, DataFileLimits.MaxDevices + 1).Select(i => new PresetEntry(@"USB\" + i, "S", 1, 2, true)).ToList();
        WriteJson(PresetPath, new[] { new Preset("Big", entries) });

        var s = new PresetStore(PresetPath);
        Assert.Empty(s.Load());
        Assert.NotNull(s.LoadProblem);
    }

    [Fact]
    public void Presets_AtTheLimit_LoadsFine()
    {
        WriteJson(PresetPath, Enumerable.Range(0, DataFileLimits.MaxPresets).Select(i => P("P" + i)).ToList());
        var s = new PresetStore(PresetPath);

        Assert.Equal(DataFileLimits.MaxPresets, s.Load().Count);
        Assert.Null(s.LoadProblem);
    }

    [Fact]
    public void Presets_SavingThe101stPreset_IsRefused_AndFileUnchanged()
    {
        var s = new PresetStore(PresetPath);
        for (var i = 0; i < DataFileLimits.MaxPresets; i++) s.Save(P("P" + i));

        var ex = Assert.Throws<InvalidOperationException>(() => s.Save(P("One too many")));
        Assert.Contains("limit is 100", ex.Message);
        Assert.Equal(DataFileLimits.MaxPresets, s.Load().Count);
    }

    [Fact]
    public void Presets_ReplacingAnExistingPresetAtTheLimit_IsAllowed()
    {
        var s = new PresetStore(PresetPath);
        for (var i = 0; i < DataFileLimits.MaxPresets; i++) s.Save(P("P" + i));

        s.Save(new Preset("p5", new[] { new PresetEntry(@"USB\B", "Other", 3, 4, false) })); // same name, other case

        var all = s.Load();
        Assert.Equal(DataFileLimits.MaxPresets, all.Count);
        Assert.Equal(@"USB\B", all.Single(p => p.Name.Equals("p5", StringComparison.OrdinalIgnoreCase)).Entries.Single().PhysicalDeviceInstanceId);
    }

    [Fact]
    public void State_AddingBeyondTheLimit_DropsTheOldestRecord()
    {
        var s = new DisabledStateStore(StatePath);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        WriteJson(StatePath, Enumerable.Range(0, DataFileLimits.MaxDevices).Select(i => Rec(@"USB\" + i, start.AddMinutes(i))).ToList());

        s.Add(Rec(@"USB\NEW", start.AddDays(1)));

        var ids = s.Load().Select(r => r.PhysicalDeviceInstanceId).ToList();
        Assert.Equal(DataFileLimits.MaxDevices, ids.Count);
        Assert.DoesNotContain(@"USB\0", ids);   // the oldest is gone
        Assert.Contains(@"USB\NEW", ids);
        Assert.Null(s.LoadProblem);             // and what was written is still loadable
    }

    [Fact]
    public void State_Add_CleansAndShortensTheName()
    {
        var s = new DisabledStateStore(StatePath);
        s.Add(Rec(@"USB\A", name: "Bad\nName" + new string('x', 500)));

        var saved = Assert.Single(s.Load());
        Assert.Equal(DataFileLimits.MaxDeviceNameLength, saved.Name.Length);
        Assert.DoesNotContain('\n', saved.Name);
        Assert.Null(s.LoadProblem);
    }

    [Fact]
    public void State_Add_RejectsEmptyOrOverlongId()
    {
        var s = new DisabledStateStore(StatePath);
        Assert.Throws<ArgumentException>(() => s.Add(Rec(" ")));
        Assert.Throws<ArgumentException>(() => s.Add(Rec(new string('x', DataFileLimits.MaxInstanceIdLength + 1))));
    }

    // ---- damaged / tampered content ----

    [Fact]
    public void State_InvalidFieldValues_FileIsIgnored()
    {
        File.WriteAllText(StatePath, """[{"PhysicalDeviceInstanceId":"USB\\A","Name":"Pad","VendorId":999999,"ProductId":2,"DisabledAtUtc":"2026-01-01T00:00:00+00:00"}]""");
        var s = new DisabledStateStore(StatePath);

        Assert.Empty(s.Load());
        Assert.Contains("out of range", s.LoadProblem);
    }

    [Fact]
    public void Presets_DuplicateNames_FileIsIgnored()
    {
        WriteJson(PresetPath, new[] { P("Race"), P("race") });
        var s = new PresetStore(PresetPath);

        Assert.Empty(s.Load());
        Assert.Contains("twice", s.LoadProblem);
    }

    [Fact]
    public void Presets_NotUtf8_FileIsIgnored()
    {
        File.WriteAllBytes(PresetPath, new byte[] { 0x5B, 0xFF, 0xFE, 0xFD, 0x5D });
        var s = new PresetStore(PresetPath);

        Assert.Empty(s.Load());
        Assert.Contains("UTF-8", s.LoadProblem);
    }

    [Fact]
    public void Presets_FileWithUtf8Bom_StillLoads()
    {
        var json = JsonSerializer.Serialize(new[] { P("Race") });
        File.WriteAllBytes(PresetPath, new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray());
        var s = new PresetStore(PresetPath);

        Assert.Equal("Race", Assert.Single(s.Load()).Name);
        Assert.Null(s.LoadProblem);
    }

    [Fact]
    public void Presets_DeeplyNestedJson_IsIgnoredWithoutThrowing()
    {
        File.WriteAllText(PresetPath, new string('[', 5000) + new string(']', 5000));
        var s = new PresetStore(PresetPath);

        Assert.Empty(s.Load());
        Assert.NotNull(s.LoadProblem);
    }

    [Fact]
    public void Problem_IsClearedOnceTheFileIsFixed()
    {
        File.WriteAllText(PresetPath, "{ broken");
        var s = new PresetStore(PresetPath);
        Assert.Empty(s.Load());
        Assert.NotNull(s.LoadProblem);

        s.Save(P("Race")); // replaces the broken file
        Assert.Single(s.Load());
        Assert.Null(s.LoadProblem);
    }

    [Fact]
    public void Problem_MessageSaysWhatHappened()
    {
        File.WriteAllText(PresetPath, "{ broken");
        var s = new PresetStore(PresetPath);
        s.Load();

        Assert.Contains("presets.json", s.LoadProblem);
        Assert.Contains("ignored", s.LoadProblem);
        Assert.Contains(".corrupt", s.LoadProblem);
    }

    // ---- links ----

    [Fact]
    public void Write_ThroughAJunctionDirectory_IsRefused_AndTargetStaysEmpty()
    {
        var target = MakeJunction("linked");
        var s = new PresetStore(Path.Combine(_dir, "linked", "presets.json"));

        Assert.Throws<IOException>(() => s.Save(P("Race")));
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Fact]
    public void Read_FromAJunctionDirectory_IsNotRefusedForTheFileItself_ButWritesAre()
    {
        // Reading a regular file inside a junctioned folder is harmless; the guard that matters is on writes.
        MakeJunction("linked2");
        var s = new DisabledStateStore(Path.Combine(_dir, "linked2", "disabled.json"));

        Assert.Empty(s.Load());
        Assert.Throws<IOException>(() => s.Add(Rec(@"USB\A")));
    }
}
