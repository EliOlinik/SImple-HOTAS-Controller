using HotasController.Core.Diagnostics;
using Xunit;

namespace HotasController.Tests;

public class SnapshotDiffTests
{
    [Fact]
    public void Compare_IdenticalDocuments_NoDifferences()
    {
        const string json = """{"nodes":{"USB\\VID_1&PID_2\\SN0001":{"service":"usbccgp","filters":["a"]}}}""";
        Assert.Empty(SnapshotDiff.Compare(json, json));
    }

    [Fact]
    public void Compare_ChangedValue_Reported()
    {
        var diff = SnapshotDiff.Compare("""{"n":{"driver":"input.inf"}}""", """{"n":{"driver":"oem97.inf"}}""");
        var d = Assert.Single(diff);
        Assert.Equal("n.driver", d.Path);
        Assert.Equal("input.inf", d.Before);
        Assert.Equal("oem97.inf", d.After);
        Assert.Equal("changed", d.Kind);
    }

    [Fact]
    public void Compare_AddedAndRemovedNodes_Reported()
    {
        var diff = SnapshotDiff.Compare("""{"nodes":{"A":{"x":1}}}""", """{"nodes":{"B":{"x":1}}}""");
        Assert.Contains(diff, d => d.Path == "nodes.A.x" && d.Kind == "removed");
        Assert.Contains(diff, d => d.Path == "nodes.B.x" && d.Kind == "added");
    }

    [Fact]
    public void Compare_RunMetadata_Ignored()
    {
        var diff = SnapshotDiff.Compare(
            """{"capturedAt":"t1","label":"before","tool":"1","data":{"v":1}}""",
            """{"capturedAt":"t2","label":"after","tool":"1","data":{"v":1}}""");
        Assert.Empty(diff);
    }

    [Fact]
    public void Compare_ArrayChange_ReportedByIndex()
    {
        var diff = SnapshotDiff.Compare("""{"f":["a"]}""", """{"f":["a","b"]}""");
        var d = Assert.Single(diff);
        Assert.Equal("f[1]", d.Path);
        Assert.Equal("added", d.Kind);
    }

    [Fact]
    public void Compare_EmptyArrayVersusFilled_Reported()
    {
        var diff = SnapshotDiff.Compare("""{"f":[]}""", """{"f":["a"]}""");
        Assert.Contains(diff, d => d.Path == "f" && d.Before == "[]" && d.After is null);
        Assert.Contains(diff, d => d.Path == "f[0]" && d.After == "a");
    }

    [Fact]
    public void Compare_InvalidJson_Throws()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SnapshotDiff.Compare("{", "{}"));
    }
}
