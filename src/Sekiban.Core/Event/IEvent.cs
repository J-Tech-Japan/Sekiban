using MediatR;
using Sekiban.Core.Document;
using Sekiban.Core.History;
namespace Sekiban.Core.Event;

public interface IEvent : INotification, ICallHistories, IDocument, IEventPayloadAccessor
{
    public string AggregateType { get; }
    public int Version { get; }

    public (IEvent, IEventPayloadCommon) GetConvertedEventAndPayload();
}
