using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Events;

/// <summary>
///     Interface that a event can be put into specific aggregate payload type.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
// ReSharper disable once UnusedTypeParameter
public interface IEventPayloadApplicableTo<TAggregatePayload> : IEventPayloadCommon where TAggregatePayload : IAggregatePayloadCommon;
