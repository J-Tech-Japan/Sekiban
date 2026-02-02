namespace Sekiban.Dcb.Primitives;

/// <summary>
///     Primitive projection runtime instance that owns its internal state.
///     Intended for external runtimes (e.g., WASM) that serialize state only on demand.
/// </summary>
public interface IPrimitiveProjectionInstance : IDisposable
{
    /// <summary>
    ///     Applies an event to the runtime.
    /// </summary>
    void ApplyEvent(
        string eventType,
        string eventPayloadJson,
        IReadOnlyList<string> tags,
        string? sortableUniqueId);

    /// <summary>
    ///     Executes a single-result query.
    /// </summary>
    string ExecuteQuery(string queryType, string queryParamsJson);

    /// <summary>
    ///     Executes a list query.
    /// </summary>
    string ExecuteListQuery(string queryType, string queryParamsJson);

    /// <summary>
    ///     Serializes the internal state for persistence.
    ///     This should be called only when persistence is required.
    /// </summary>
    string SerializeState();

    /// <summary>
    ///     Restores internal state from a serialized representation.
    /// </summary>
    void RestoreState(string stateJson);
}
