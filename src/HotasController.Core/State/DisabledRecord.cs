namespace HotasController.Core.State;

/// <summary>A physical device this app disabled, kept so it can be re-enabled later.</summary>
public sealed record DisabledRecord(
    string PhysicalDeviceInstanceId,
    string Name,
    int? VendorId,
    int? ProductId,
    DateTimeOffset DisabledAtUtc);
