using HotasController.Core;

namespace HotasController.Tests;

public class InstanceIdParserTests
{
    [Theory]
    [InlineData(@"USB\VID_054C&PID_0CE6&MI_03\6&1590F831&2&0003", true)]
    [InlineData(@"USB\VID_054C&PID_0CE6&mi_03\6&1590F831&2&0003", true)]
    [InlineData(@"USB\VID_0738&PID_2221\SN0001", false)]
    [InlineData(@"HID\VID_054C&PID_0CE6&MI_03\7&38102E58&0&0000", true)]
    public void IsCompositeInterface_detects_MI_nodes(string id, bool expected) =>
        Assert.Equal(expected, InstanceIdParser.IsCompositeInterface(id));

    [Theory]
    [InlineData(@"HID\VID_054C&PID_0CE6&MI_03\7&38102E58&0&0000", 0x054C, 0x0CE6)]
    [InlineData(@"USB\vid_0738&pid_2221\SN0001", 0x0738, 0x2221)]
    public void TryGetVidPid_parses(string id, int vid, int pid)
    {
        Assert.True(InstanceIdParser.TryGetVidPid(id, out var v, out var p));
        Assert.Equal((ushort)vid, v);
        Assert.Equal((ushort)pid, p);
    }

    [Theory]
    [InlineData(@"ROOT\SYSTEM\0002")]
    [InlineData(null)]
    public void TryGetVidPid_rejects_non_vidpid(string? id) =>
        Assert.False(InstanceIdParser.TryGetVidPid(id, out _, out _));
}
