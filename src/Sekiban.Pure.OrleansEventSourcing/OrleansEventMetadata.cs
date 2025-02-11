using Sekiban.Pure.Events;
namespace Sekiban.Pure.OrleansEventSourcing;

[GenerateSerializer]
public record OrleansEventMetadata([property:Id(0)]string CausationId,
    [property:Id(1)]string CorrelationId, [property:Id(2)]string ExecutedUser)
{
    public static OrleansEventMetadata FromEventMetadata(EventMetadata metadata) => new(metadata.CausationId, metadata.CorrelationId, metadata.ExecutedUser);
    public EventMetadata ToEventMetadata() => new(CausationId, CorrelationId, ExecutedUser);
}