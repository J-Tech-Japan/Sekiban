using MediatR;
using Sekiban.Core.Documents;
using Sekiban.Core.History;
namespace Sekiban.Core.Events;

public interface IEvent : INotification, ICallHistories, IAggregateDocument, IEventPayloadAccessor
{
    public int Version { get; }

    public (IEvent, IEventPayloadCommon) GetConvertedEventAndPayload();
}
