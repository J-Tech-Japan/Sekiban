using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Events;

// ReSharper disable once UnusedTypeParameter
public interface IEventPayloadApplicableTo<TAggregatePayload> : IEventPayloadCommon where TAggregatePayload : IAggregatePayloadCommon
{
}
