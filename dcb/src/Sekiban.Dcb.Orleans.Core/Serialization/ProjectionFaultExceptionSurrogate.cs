using Sekiban.Dcb.Actors;
namespace Sekiban.Dcb.Orleans.Serialization;

/// <summary>
///     Lets a <see cref="SekibanProjectionFaultException" /> cross an Orleans grain boundary as a ResultBox field.
///     The exception is a Core type (no Orleans dependency), and it carries a <see cref="ProjectionFaultDescriptor" />,
///     so Orleans has no field codec for it. This surrogate flattens the descriptor into serializable fields and
///     rebuilds the exception on the far side — so a faulted query result keeps its type and its fault context across
///     the boundary, exactly as the packet asks (no reference-identity promise).
/// </summary>
[GenerateSerializer]
public struct ProjectionFaultExceptionSurrogate
{
    [Id(0)] public Guid EventId { get; set; }
    [Id(1)] public string EventType { get; set; }
    [Id(2)] public string ProjectorName { get; set; }
    [Id(3)] public string Position { get; set; }
    [Id(4)] public string Message { get; set; }
    [Id(5)] public long FaultedAtUtc { get; set; }
}

[RegisterConverter]
public sealed class ProjectionFaultExceptionConverter
    : IConverter<SekibanProjectionFaultException, ProjectionFaultExceptionSurrogate>
{
    public SekibanProjectionFaultException ConvertFromSurrogate(in ProjectionFaultExceptionSurrogate surrogate)
    {
        var fault = new ProjectionFaultDescriptor(
            surrogate.EventId,
            surrogate.EventType ?? string.Empty,
            surrogate.ProjectorName ?? string.Empty,
            surrogate.Position ?? string.Empty,
            surrogate.Message ?? string.Empty,
            surrogate.FaultedAtUtc);
        var exception = new SekibanProjectionFaultException(fault);

        // The constructor has already annotated this exception. Re-apply at the transport reconstruction boundary on
        // purpose: it is idempotent, and it prevents future constructor/converter drift from dropping client-visible
        // fault context after Orleans deserialization.
        fault.AnnotateReRaise(exception);
        return exception;
    }

    public ProjectionFaultExceptionSurrogate ConvertToSurrogate(in SekibanProjectionFaultException value)
    {
        value.Fault.AnnotateReRaise(value);
        return new()
        {
            EventId = value.Fault.EventId,
            EventType = value.Fault.EventType,
            ProjectorName = value.Fault.ProjectorName,
            Position = value.Fault.Position,
            Message = value.Fault.Message,
            FaultedAtUtc = value.Fault.FaultedAtUtc
        };
    }
}
