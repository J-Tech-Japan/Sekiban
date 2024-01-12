using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Handler Interface for　<see cref="ICommandWithoutLoadingAggregate{TAggregatePayload}" />
///     Application developer can implement this interface to define a command handler
///     A Command Handler can receive DI objects through constructor.
/// </summary>
/// <typeparam name="TAggregatePayload">Target Aggregate</typeparam>
/// <typeparam name="TCommand">Target Command</typeparam>
public interface ICommandWithoutLoadingAggregateHandlerAsync<TAggregatePayload, TCommand> : ICommandHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload> where TCommand : ICommandWithoutLoadingAggregate<TAggregatePayload>
{
    /// <summary>
    ///     Command Handler. In this method, you can not see the current state of the aggregate.
    /// </summary>
    /// <param name="aggregateId">Aggregate Id</param>
    /// <param name="command">Executing Command</param>
    /// <returns></returns>
    public IAsyncEnumerable<IEventPayloadApplicableTo<TAggregatePayload>> HandleCommandAsync(Guid aggregateId, TCommand command);
}