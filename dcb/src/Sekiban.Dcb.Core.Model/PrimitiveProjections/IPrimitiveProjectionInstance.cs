using System.Text;

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
    ///     Applies multiple events to the runtime.
    ///     Implementations may override this to use a more efficient batch ABI.
    /// </summary>
    void ApplyEvents(IReadOnlyList<PrimitiveProjectionEventEnvelope> events)
    {
        foreach (var ev in events)
        {
            ApplyEvent(ev.EventType, ev.EventPayloadJson, ev.Tags, ev.SortableUniqueId);
        }
    }

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
    ///     Serializes the internal state as UTF-8 bytes.
    ///     Implementations can override this to avoid materializing a UTF-16 string.
    /// </summary>
    byte[] SerializeStateUtf8() => Encoding.UTF8.GetBytes(SerializeState());

    /// <summary>
    ///     Restores internal state from a serialized representation.
    /// </summary>
    void RestoreState(string stateJson);

    /// <summary>
    ///     Restores the internal state from UTF-8 bytes.
    ///     Implementations can override this to avoid materializing a UTF-16 string.
    /// </summary>
    void RestoreStateUtf8(byte[] stateJsonUtf8) => RestoreState(Encoding.UTF8.GetString(stateJsonUtf8));
}
