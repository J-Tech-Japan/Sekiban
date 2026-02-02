using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Primitives;

/// <summary>
///     JSON payload wrapper for primitive projection runtimes.
/// </summary>
public sealed record PrimitiveJsonMultiProjectionPayload(string ProjectorName, string Json) : IMultiProjectionPayload;
