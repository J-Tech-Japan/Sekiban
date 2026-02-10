using System.Text;
using System.Text.Json;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# implementation of IProjectionState.
///     Wraps a DualStateProjectionWrapper (accessed via IDualStateAccessor) to provide
///     safe/unsafe version and payload information.
/// </summary>
public class NativeProjectionState : IProjectionState
{
    private readonly IMultiProjectionPayload _payload;
    private readonly object? _safePayload;
    private readonly object? _unsafePayload;

    public NativeProjectionState(
        IMultiProjectionPayload payload,
        int safeVersion,
        int unsafeVersion,
        string? safeLastSortableUniqueId,
        string? lastSortableUniqueId,
        Guid? lastEventId,
        object? safePayload = null,
        object? unsafePayload = null)
    {
        _payload = payload;
        SafeVersion = safeVersion;
        UnsafeVersion = unsafeVersion;
        SafeLastSortableUniqueId = safeLastSortableUniqueId;
        LastSortableUniqueId = lastSortableUniqueId;
        LastEventId = lastEventId;
        _safePayload = safePayload;
        _unsafePayload = unsafePayload;
    }

    /// <summary>
    ///     Create from a DualStateProjectionWrapper via IDualStateAccessor interface.
    /// </summary>
    public static NativeProjectionState FromDualStateAccessor(IDualStateAccessor accessor)
    {
        var wrapper = accessor as IMultiProjectionPayload
            ?? throw new InvalidOperationException(
                "IDualStateAccessor must also implement IMultiProjectionPayload");

        return new NativeProjectionState(
            wrapper,
            accessor.SafeVersion,
            accessor.UnsafeVersion,
            accessor.SafeLastSortableUniqueId,
            accessor.UnsafeLastSortableUniqueId,
            accessor.UnsafeLastEventId,
            safePayload: accessor.GetSafeProjectorPayload(),
            unsafePayload: accessor.GetUnsafeProjectorPayload());
    }

    public static NativeProjectionState FromInitialPayload(IMultiProjectionPayload payload) =>
        new(payload, 0, 0, null, null, null, payload, payload);

    public int SafeVersion { get; }
    public int UnsafeVersion { get; }
    public string? SafeLastSortableUniqueId { get; }
    public string? LastSortableUniqueId { get; }
    public Guid? LastEventId { get; }

    /// <summary>
    ///     The underlying IMultiProjectionPayload (DualStateProjectionWrapper or raw payload).
    /// </summary>
    public IMultiProjectionPayload Payload => _payload;

    public object? GetSafePayload() => _safePayload;
    public object? GetUnsafePayload() => _unsafePayload;

    public long EstimatePayloadSizeBytes(JsonSerializerOptions? options)
    {
        var json = JsonSerializer.Serialize(
            _payload,
            _payload.GetType(),
            options ?? JsonSerializerOptions.Default);
        return Encoding.UTF8.GetByteCount(json);
    }
}
