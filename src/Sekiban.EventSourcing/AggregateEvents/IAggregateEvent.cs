using MediatR;
namespace Sekiban.EventSourcing.AggregateEvents;

public interface IAggregateEvent : INotification, ICallHistories, IDocument
{
    public Guid AggregateId { get; }
    public string AggregateType { get; }

    public bool IsAggregateInitialEvent { get; }

    public int Version { get; }

    public IEventPayload Payload { get; }
    public dynamic GetComparableObject(IAggregateEvent original, bool copyVersion = true);
}
