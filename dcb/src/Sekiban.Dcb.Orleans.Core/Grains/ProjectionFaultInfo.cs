namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     The committed identity of a projection fault for the operator-only read/reset flow. This deliberately mirrors
///     transport-safe scalar fields instead of exposing the Core-only <c>ProjectionFaultDescriptor</c> across Orleans.
/// </summary>
[GenerateSerializer]
public sealed record ProjectionFaultInfo(
    [property: Id(0)] string ProjectorName,
    [property: Id(1)] Guid FaultEventId,
    [property: Id(2)] string EventType,
    [property: Id(3)] string Position,
    [property: Id(4)] string Message,
    [property: Id(5)] DateTime FirstObservedUtc);

/// <summary>
///     Successful payload of <see cref="IMultiProjectionGrain.TryGetProjectionFaultAsync" />. A false
///     <see cref="HasFault" /> is the sole supported+no-fault state and means only that no descriptor exists in the
///     committed grain record; failure to read and a legacy implementation remain ResultBox errors.
/// </summary>
[GenerateSerializer]
public sealed record ProjectionFaultReadResult(
    [property: Id(0)] bool HasFault,
    [property: Id(1)] ProjectionFaultInfo? Fault)
{
    /// <summary>The canonical successful no-fault value; it is not used to conceal unsupported or read failures.</summary>
    public static ProjectionFaultReadResult NoCommittedFault { get; } = new(false, null);
}
