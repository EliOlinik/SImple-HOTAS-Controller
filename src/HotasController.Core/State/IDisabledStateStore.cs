namespace HotasController.Core.State;

public interface IDisabledStateStore
{
    IReadOnlyList<DisabledRecord> Load();

    /// <summary>Adds the record, replacing any existing one with the same id (case-insensitive).</summary>
    void Add(DisabledRecord record);

    /// <summary>Removes the record with this id; no-op if absent.</summary>
    void Remove(string physicalDeviceInstanceId);

    void Clear();
}
