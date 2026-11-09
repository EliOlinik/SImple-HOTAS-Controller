using HotasController.Core.Presets;
using HotasController.Core.State;
using HotasController.Core.Storage;

namespace HotasController.Tests;

public sealed class DataFileValidatorTests
{
    private static PresetEntry Entry(string id = @"USB\VID_1&PID_2\1", string name = "Stick", int? vid = 1, int? pid = 2) =>
        new(id, name, vid, pid, true);

    private static Preset P(string name = "Race", params PresetEntry[] entries) =>
        new(name, entries.Length > 0 ? entries : new[] { Entry() });

    private static DisabledRecord Rec(string id = @"USB\A", string name = "Pad", int? vid = 1, int? pid = 2) =>
        new(id, name, vid, pid, DateTimeOffset.UnixEpoch);

    // ---- presets ----

    [Fact]
    public void Presets_Valid_ReturnsNull() => Assert.Null(DataFileValidator.ValidatePresets(new[] { P("A"), P("B") }));

    [Fact]
    public void Presets_EmptyList_IsValid() => Assert.Null(DataFileValidator.ValidatePresets(Array.Empty<Preset>()));

    [Fact]
    public void Presets_Null_IsRejected() => Assert.NotNull(DataFileValidator.ValidatePresets(null));

    [Fact]
    public void Presets_ExactlyAtLimit_IsValid() =>
        Assert.Null(DataFileValidator.ValidatePresets(Enumerable.Range(0, DataFileLimits.MaxPresets).Select(i => P("P" + i)).ToList()));

    [Fact]
    public void Presets_OverLimit_IsRejected()
    {
        var problem = DataFileValidator.ValidatePresets(Enumerable.Range(0, DataFileLimits.MaxPresets + 1).Select(i => P("P" + i)).ToList());
        Assert.Contains("limit is 100", problem);
    }

    [Fact]
    public void Presets_DevicesAtLimit_IsValid()
    {
        var entries = Enumerable.Range(0, DataFileLimits.MaxDevices).Select(i => Entry(@"USB\" + i)).ToArray();
        Assert.Null(DataFileValidator.ValidatePresets(new[] { P("Big", entries) }));
    }

    [Fact]
    public void Presets_TooManyDevices_IsRejected()
    {
        var entries = Enumerable.Range(0, DataFileLimits.MaxDevices + 1).Select(i => Entry(@"USB\" + i)).ToArray();
        Assert.Contains("limit is 1000", DataFileValidator.ValidatePresets(new[] { P("Big", entries) }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Presets_BlankName_IsRejected(string name) => Assert.NotNull(DataFileValidator.ValidatePresets(new[] { P(name) }));

    [Fact]
    public void Presets_NameTooLong_IsRejected() =>
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { P(new string('x', DataFileLimits.MaxPresetNameLength + 1)) }));

    [Fact]
    public void Presets_NameAtMaxLength_IsValid() =>
        Assert.Null(DataFileValidator.ValidatePresets(new[] { P(new string('x', DataFileLimits.MaxPresetNameLength)) }));

    [Fact]
    public void Presets_NameWithControlCharacter_IsRejected() =>
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { P("Ra\ncing") }));

    [Fact]
    public void Presets_DuplicateNamesIgnoringCase_AreRejected() =>
        Assert.Contains("twice", DataFileValidator.ValidatePresets(new[] { P("Race"), P("RACE") }));

    [Fact]
    public void Presets_NullPresetOrNullEntry_IsRejected()
    {
        Assert.NotNull(DataFileValidator.ValidatePresets(new Preset?[] { null }));
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { new Preset("A", new PresetEntry?[] { null }!) }));
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { new Preset("A", null!) }));
    }

    [Fact]
    public void Presets_BadDeviceFields_AreRejected()
    {
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { P("A", Entry(id: "")) }));
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { P("A", Entry(id: new string('x', DataFileLimits.MaxInstanceIdLength + 1))) }));
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { P("A", Entry(name: new string('x', DataFileLimits.MaxDeviceNameLength + 1))) }));
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { P("A", Entry(name: "bad\u0000name")) }));
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { P("A", Entry(vid: 0x10000)) }));
        Assert.NotNull(DataFileValidator.ValidatePresets(new[] { P("A", Entry(pid: -1)) }));
    }

    [Fact]
    public void Presets_NullVidPid_IsValid() =>
        Assert.Null(DataFileValidator.ValidatePresets(new[] { P("A", Entry(vid: null, pid: null)) }));

    // ---- disabled-device records ----

    [Fact]
    public void Records_Valid_ReturnsNull() => Assert.Null(DataFileValidator.ValidateDisabledRecords(new[] { Rec(@"USB\A"), Rec(@"USB\B") }));

    [Fact]
    public void Records_AtLimit_IsValid() =>
        Assert.Null(DataFileValidator.ValidateDisabledRecords(Enumerable.Range(0, DataFileLimits.MaxDevices).Select(i => Rec(@"USB\" + i)).ToList()));

    [Fact]
    public void Records_OverLimit_IsRejected() =>
        Assert.Contains("limit is 1000", DataFileValidator.ValidateDisabledRecords(
            Enumerable.Range(0, DataFileLimits.MaxDevices + 1).Select(i => Rec(@"USB\" + i)).ToList()));

    [Fact]
    public void Records_DuplicateIdsIgnoringCase_AreRejected() =>
        Assert.Contains("twice", DataFileValidator.ValidateDisabledRecords(new[] { Rec(@"USB\A"), Rec(@"usb\a") }));

    [Fact]
    public void Records_NullAndBadFields_AreRejected()
    {
        Assert.NotNull(DataFileValidator.ValidateDisabledRecords(null));
        Assert.NotNull(DataFileValidator.ValidateDisabledRecords(new DisabledRecord?[] { null }));
        Assert.NotNull(DataFileValidator.ValidateDisabledRecords(new[] { Rec(id: " ") }));
        Assert.NotNull(DataFileValidator.ValidateDisabledRecords(new[] { Rec(vid: 70000) }));
    }
}
