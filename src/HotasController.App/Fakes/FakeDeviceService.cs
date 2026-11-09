using HotasController.Core.Abstractions;
using HotasController.Core.Models;

namespace HotasController.App.Fakes;

/// <summary>In-memory controllers for UI development. Shares state with <see cref="FakeDeviceToggler"/>.</summary>
public sealed class FakeDeviceService : IDeviceService
{
    private readonly object _gate = new();
    private List<GameController> _controllers = new()
    {
        Make("USB\\VID_0738&PID_2221\\FAKE1", "Saitek Pro Flight X-56 Rhino Stick", 0x0738, 0x2221, 2, companions: 1),
        Make("USB\\VID_0738&PID_A221\\FAKE2", "Saitek Pro Flight X-56 Rhino Throttle", 0x0738, 0xA221, 2, companions: 1),
        Make("USB\\VID_231D&PID_0200\\FAKE3", "VKB Gladiator NXT EVO", 0x231D, 0x0200, 1),
        Make("BTHENUM\\{00001124-0000-1000-8000-00805F9B34FB}_VID&0002054C_PID&0CE6\\FAKE4", "DualSense Wireless Controller", 0x054C, 0x0CE6, 1),
    };

    public IReadOnlyList<GameController> GetGameControllers()
    {
        lock (_gate) return _controllers.ToList();
    }

    internal void SetEnabled(GameController controller, bool enabled)
    {
        lock (_gate)
        {
            _controllers = _controllers
                .Select(c => c.PhysicalDeviceInstanceId == controller.PhysicalDeviceInstanceId
                    ? c with
                    {
                        Nodes = c.Nodes.Select(n => n with { IsDisabled = !enabled }).ToList(),
                        CompanionNodes = c.CompanionNodes.Select(n => n with { IsDisabled = !enabled }).ToList(),
                    }
                    : c)
                .ToList();
        }
    }

    private static GameController Make(string id, string name, ushort vid, ushort pid, int nodeCount, int companions = 0)
    {
        var nodes = Enumerable.Range(0, nodeCount)
            .Select(i => new HidNode($"{id}\\NODE{i}", "HID-compliant game controller", id, name, vid, pid, false,
                new HidUsage(HidUsage.GenericDesktopPage, 0x04)))
            .ToList();
        var extra = Enumerable.Range(0, companions)
            .Select(i => new HidNode($"{id}\\BULK{i}", "Vendor bulk interface", id, name, vid, pid, false, null))
            .ToList();
        return new GameController(id, name, vid, pid, nodes) { CompanionNodes = extra };
    }
}
