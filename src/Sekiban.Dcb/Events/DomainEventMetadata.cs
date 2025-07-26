namespace Sekiban.Dcb.Events;

public record DomainEventMetadata(string CausationId, string CorrelationId, string ExecutedUser)
{
}