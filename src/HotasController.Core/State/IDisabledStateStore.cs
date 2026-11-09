namespace HotasController.Core.State;

public interface IDisabledStateStore
{
    IReadOnlyList<DisabledRecord> Load();

    /// <summary>
    /// Null when the last load was fine. Otherwise a message for the user saying the file was ignored and why
    /// (damaged, too large, over the limits, a link, unreadable).
    /// </summary>
    string? LoadProblem { get; }

    /// <summary>
    /// Adds the record, replacing any existing one with the same id (case-insensitive). The name is cleaned and cut to the
    /// allowed length, and when the list would exceed the limit the oldest record is dropped.
    /// </summary>
    void Add(DisabledRecord record);

    /// <summary>Removes the record with this id; no-op if absent.</summary>
    void Remove(string physicalDeviceInstanceId);

    void Clear();
}
