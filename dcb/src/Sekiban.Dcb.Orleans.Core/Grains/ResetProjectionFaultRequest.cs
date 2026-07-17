namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Operator-only request to clear a persisted projection fault and rebuild. It carries the EXACT identity of the
///     fault the operator observed — projector name, fault event id, and fault stream position — as a concurrency
///     token. The grain accepts the reset only when this token matches the CURRENT PERSISTED fault descriptor (the
///     concurrency authority), so a stale token, a changed descriptor, or a wrong projector is rejected with no write.
///     Acquire the token from the fault context surfaced on a failed query (the projection-fault error carries the
///     event id, type, projector and position); do not synthesise it. This type is additive and admin-plane only — it
///     is never invoked automatically and is not exposed through <c>ISekibanExecutor</c>.
/// </summary>
[GenerateSerializer]
public sealed record ResetProjectionFaultRequest(
    [property: Id(0)] string ProjectorName,
    [property: Id(1)] string FaultEventId,
    [property: Id(2)] string FaultPosition);
