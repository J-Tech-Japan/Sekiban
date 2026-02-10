using System.Text.Json;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Abstracts the projection state for both C# native and WASM runtimes.
///     C# implementation wraps DualStateProjectionWrapper.
///     WASM implementation manages metadata while state lives in the WASM instance.
/// </summary>
public interface IProjectionState
{
    int SafeVersion { get; }
    int UnsafeVersion { get; }
    string? SafeLastSortableUniqueId { get; }
    string? LastSortableUniqueId { get; }
    Guid? LastEventId { get; }
    object? GetSafePayload();
    object? GetUnsafePayload();
    long EstimatePayloadSizeBytes(JsonSerializerOptions? options);
}
