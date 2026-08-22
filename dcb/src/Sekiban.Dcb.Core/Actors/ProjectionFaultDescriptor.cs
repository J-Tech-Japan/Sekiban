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

    /// <summary>
    ///     The machine-readable marker on a reconstructed fault. It is deliberately absent from the original exception
    ///     thrown at the fold boundary: only <see cref="SekibanProjectionFaultException" /> instances are re-raises.
    /// </summary>
    public const string ReRaiseDataKey = "Sekiban.Projection.IsReRaise";

    /// <summary>A one-line, secret-free summary for a log line or an exception message.</summary>
    public string Describe() =>
        $"projector '{ProjectorName}' is faulted at event {EventId} ({EventType}), position {Position}: {Message}";

    /// <summary>Formats the distinct message carried by a fault reconstructed from a persisted descriptor.</summary>
    public string DescribeReRaise() =>
        $"projector '{ProjectorName}' was previously faulted at event {EventId} ({EventType}), position {Position} "
        + $"(first observed {new DateTime(FaultedAtUtc, DateTimeKind.Utc):O}): {Message}";

    /// <summary>
    ///     Writes the four original fault identifiers onto an exception's <see cref="System.Exception.Data" />. This is
    ///     intentionally safe to repeat: a partially annotated exception is completed, while caller-owned values are
    ///     not overwritten.
    /// </summary>
    public void Annotate(Exception exception) => AnnotateCore(exception, isReRaise: false);

    /// <summary>
    ///     Writes the four original identifiers plus the re-raise marker. Constructors and Orleans surrogate
    ///     reconstruction both call this method; its idempotence is what keeps the client-side exception complete even
    ///     when those paths overlap.
    /// </summary>
    public void AnnotateReRaise(Exception exception) => AnnotateCore(exception, isReRaise: true);

    private void AnnotateCore(Exception exception, bool isReRaise)
    {
        try
        {
            if (exception.Data.IsReadOnly)
            {
                return;
            }

            if (!exception.Data.Contains(OperationDataKey))
            {
                exception.Data[OperationDataKey] = $"MultiProjection.Fold ({ProjectorName})";
            }

            if (!exception.Data.Contains(TargetDataKey))
            {
                exception.Data[TargetDataKey] = EventType;
            }

            if (!exception.Data.Contains(EventIdDataKey))
            {
                exception.Data[EventIdDataKey] = EventId.ToString();
            }

            if (!exception.Data.Contains(PositionDataKey))
            {
                exception.Data[PositionDataKey] = Position;
            }

            if (isReRaise)
            {
                // The marker is the type's contract, not caller-provided context. Repeating this assignment is
                // idempotent and prevents a malformed pre-existing false value from masquerading as an original fault.
                exception.Data[ReRaiseDataKey] = true;
            }
        }
        catch (Exception)
        {
            // Annotation is diagnostic; never let it replace the real failure.
        }
    }
}
