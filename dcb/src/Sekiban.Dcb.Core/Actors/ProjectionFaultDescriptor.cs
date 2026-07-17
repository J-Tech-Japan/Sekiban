namespace Sekiban.Dcb.Actors;

/// <summary>
///     What went wrong when a projection could not fold an event, captured at the one place it can be named: the
///     per-event apply boundary, BEFORE the event was applied, so the descriptor points at the poison event rather
///     than at whatever ran afterward.
///     It is a plain serializable record, deliberately separate from the exception that carried the failure. In
///     process the original exception instance is what gets rethrown (never re-wrapped); the descriptor is the part
///     that can be persisted and transported across an Orleans boundary where the exception instance cannot. It holds
///     identifiers, never payload values — a fault is diagnosed by "which event, which projector, which position", not
///     by what the event contained.
/// </summary>
/// <param name="EventId">The id of the event whose fold threw.</param>
/// <param name="EventType">The event type name.</param>
/// <param name="ProjectorName">The multi-projector that could not fold it.</param>
/// <param name="Position">The event's SortableUniqueId — where in the stream the projection is stuck.</param>
/// <param name="Message">The failure message (no payload values).</param>
/// <param name="FaultedAtUtc">When the fault was first observed, in UTC ticks (serialization-friendly).</param>
public sealed record ProjectionFaultDescriptor(
    Guid EventId,
    string EventType,
    string ProjectorName,
    string Position,
    string Message,
    long FaultedAtUtc)
{
    /// <summary>
    ///     The <see cref="System.Exception.Data" /> keys the descriptor writes onto a surfaced exception. They match the
    ///     boundary-context keys SEK-G9's GuardedUnwrap already uses, so a fault that reaches a WithoutResult boundary
    ///     reads the same way as any other annotated failure. Duplicated as literals here because the GuardedUnwrap
    ///     constants are internal to another assembly; kept identical on purpose.
    /// </summary>
    public const string OperationDataKey = "Sekiban.Boundary.Operation";

    /// <summary>Companion key: what the faulted operation was working on.</summary>
    public const string TargetDataKey = "Sekiban.Boundary.Target";

    /// <summary>The <see cref="System.Exception.Data" /> key carrying the faulted event's id.</summary>
    public const string EventIdDataKey = "Sekiban.Projection.FaultEventId";

    /// <summary>The <see cref="System.Exception.Data" /> key carrying the faulted position.</summary>
    public const string PositionDataKey = "Sekiban.Projection.FaultPosition";

    /// <summary>A one-line, secret-free summary for a log line or an exception message.</summary>
    public string Describe() =>
        $"projector '{ProjectorName}' is faulted at event {EventId} ({EventType}), position {Position}: {Message}";

    /// <summary>Writes the fault's identifiers onto an exception's <see cref="System.Exception.Data" />, first-writer-wins.</summary>
    public void Annotate(Exception exception)
    {
        try
        {
            if (exception.Data.IsReadOnly || exception.Data.Contains(OperationDataKey))
            {
                return;
            }

            exception.Data[OperationDataKey] = $"MultiProjection.Fold ({ProjectorName})";
            exception.Data[TargetDataKey] = EventType;
            exception.Data[EventIdDataKey] = EventId.ToString();
            exception.Data[PositionDataKey] = Position;
        }
        catch (Exception)
        {
            // Annotation is diagnostic; never let it replace the real failure.
        }
    }
}
