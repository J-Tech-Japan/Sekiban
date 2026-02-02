namespace Sekiban.Dcb.Primitives;

/// <summary>
///     Factory for creating primitive projection runtime instances (e.g., WASM-backed).
/// </summary>
public interface IPrimitiveProjectionHost
{
    /// <summary>
    ///     Creates a new runtime instance for the specified projector.
    /// </summary>
    IPrimitiveProjectionInstance CreateInstance(string projectorName);
}
