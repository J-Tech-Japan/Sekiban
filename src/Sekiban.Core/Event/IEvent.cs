using MediatR;
using Sekiban.Core.Document;
using Sekiban.Core.History;
namespace Sekiban.Core.Event;

public interface IEvent : INotification, ICallHistories, IDocument, IEventPayloadHolder
{
    public string AggregateType { get; }

    public bool IsAggregateInitialEvent { get; }

    public int Version { get; }

    public dynamic GetComparableObject(IEvent original, bool copyVersion = true);
}
