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
    [InlineData(@"BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\7&3558921C&0&AABBCCDDEEFF_C00000000", 0x054C, 0x0CE6)]
    [InlineData(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\8&187324E9&0&0000", 0x054C, 0x0CE6)]
    [InlineData(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_vid&0001045e_pid&02fd\9&1A2B3C4D&0&0000", 0x045E, 0x02FD)]
    public void TryGetVidPid_parses_bluetooth_form(string id, int vid, int pid)
    {
        Assert.True(InstanceIdParser.TryGetVidPid(id, out var v, out var p));
        Assert.Equal((ushort)vid, v);
        Assert.Equal((ushort)pid, p);
    }

    [Theory]
    [InlineData(@"ROOT\SYSTEM\0002")]
    [InlineData(@"BTH\MS_BTHBRB\6&1962176B&0&1")]
    [InlineData(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C\8&1&0&0000")]
    [InlineData(null)]
    public void TryGetVidPid_rejects_non_vidpid(string? id) =>
        Assert.False(InstanceIdParser.TryGetVidPid(id, out _, out _));

    [Theory]
    [InlineData(@"BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\7&3558921C&0&AABBCCDDEEFF_C00000000", true)]
    [InlineData(@"HID\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\8&187324E9&0&0000", true)]
    [InlineData(@"BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&01054C_PID&0CE6\8&1&0&0000", true)]
    [InlineData(@"HID\{00001812-0000-1000-8000-00805f9b34fb}_VID&02045E_PID&0B13\9&1&0&0000", true)]
    [InlineData(@"USB\VID_054C&PID_0CE6\SN0001", false)]
    [InlineData(@"HID\VID_054C&PID_0CE6&MI_03\7&38102E58&0&0000", false)]
    [InlineData(@"ROOT\SYSTEM\0002", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBluetooth_detects_bluetooth_ids(string? id, bool expected) =>
        Assert.Equal(expected, InstanceIdParser.IsBluetooth(id));

    [Theory]
    [InlineData(@"USB\VID_054C&PID_0CE6\SN0001", true)]
    [InlineData(@"usb\vid_0738&pid_2221\k0422622", true)]
    [InlineData(@"BTHENUM\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\7&1&AABBCCDDEEFF", false)]
    [InlineData(@"HID\VID_054C&PID_0CE6&MI_03\7&38102E58&0&0000", false)]
    [InlineData(@"USBSTOR\Disk&Ven_X\1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsUsb_detects_usb_ids(string? id, bool expected) =>
        Assert.Equal(expected, InstanceIdParser.IsUsb(id));

    [Theory]
    [InlineData(@"USB\VID_054C&PID_0CE6\SN0001", "USB")]
    [InlineData(@"BTHENUM\{00001124}_VID&0002054C_PID&0CE6\7&1", "BTHENUM")]
    [InlineData("NOBACKSLASH", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void GetBus_returns_text_before_first_backslash(string? id, string expected) =>
        Assert.Equal(expected, InstanceIdParser.GetBus(id));
}
