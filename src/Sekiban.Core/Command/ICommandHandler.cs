using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Handler Interface for ICommand
///     Application developer can implement this interface to define a command handler
///     A Command Handler can receive DI objects through constructor.
/// </summary>
/// <typeparam name="TAggregatePayload">Target Aggregate</typeparam>
/// <typeparam name="TCommand">Target Command</typeparam>
public interface ICommandHandler<TAggregatePayload, TCommand> : ICommandHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon where TCommand : ICommand<TAggregatePayload>
{
    /// <summary>
    ///     A Command Handler.
    ///     Persistent event can be done using yield return.
    ///     e.g. yield return new Event1();
    ///     Application developer can return multiple event.
    ///     All event will be applied to the one Aggregate type with defined AggregateId.
    /// </summary>
    /// <param name="getAggregateState">
    ///     Call this function anytime during command handler to receive current aggregate.
    ///     After yield return event, already received aggregate will not updated,
    ///     but you can call this function again to receive updated aggregate.
    /// </param>
    /// <param name="command"></param>
    /// <returns></returns>
    public IAsyncEnumerable<IEventPayloadApplicableTo<TAggregatePayload>> HandleCommandAsync(
        Func<AggregateState<TAggregatePayload>> getAggregateState,
        TCommand command);
}
