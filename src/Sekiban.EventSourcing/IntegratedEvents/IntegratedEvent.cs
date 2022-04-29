using Sekiban.EventSourcing.Histories;
namespace Sekiban.EventSourcing.IntegratedEvents;

public class IntegratedEvent : IIntegratedEvent, ICallHistories
{
    public List<CallHistory> CallHistories { get; init; } = new();
}
