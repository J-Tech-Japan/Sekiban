namespace Sekiban.Dcb.Primitives;

/// <summary>
///     Factory for creating primitive projection runtime instances (e.g., WASM-backed).
/// </summary>
public interface IPrimitiveProjectionHost
{
    /// <summary>
    ///     Creates a new runtime instance for the specified projector.
    ///     May create a new instance or return a pooled one.
    /// </summary>
    IPrimitiveProjectionInstance CreateInstance(string projectorName);

    /// <summary>
    ///     Creates a runtime instance, waiting if the pool is at capacity.
    ///     Use this in async contexts (e.g., Orleans grains) to avoid blocking scheduler threads.
    ///     Default implementation delegates to the synchronous <see cref="CreateInstance"/>.
    /// </summary>
    ValueTask<IPrimitiveProjectionInstance> CreateInstanceAsync(string projectorName, CancellationToken ct = default)
        => ValueTask.FromResult(CreateInstance(projectorName));
}
