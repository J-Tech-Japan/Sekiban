using MediatR;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.History;
namespace Sekiban.Core.Events;

/// <summary>
///     General Event Interface
///     This is commonly used to group all events
/// </summary>
public interface IEvent : INotification, ICallHistories, IAggregateDocument, IEventPayloadAccessor
{
    /// <summary>
    ///     Event Version.
    ///     If Event is produced without concurrency backend (two different events or
    ///     <see cref="ICommandWithoutLoadingAggregate{TAggregatePayload}" />
    /// </summary>
    public int Version { get; }
    /// <summary>
    ///     Get Converted EventAndPayload
    /// </summary>
    /// <returns></returns>
    public (IEvent, IEventPayloadCommon) GetConvertedEventAndPayload();
}
