using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Handler Interface for ICommand
///     Application developer can implement this interface to define a command handler
///     A Command Handler can receive DI objects through constructor.
///     Handler is not async. If awaiting is required, use
///     <see cref="IVersionValidationCommandHandlerAsync{TAggregatePayload,TCommand}" />
/// </summary>
/// <typeparam name="TAggregatePayload">Target Aggregate</typeparam>
/// <typeparam name="TCommand">Target Command</typeparam>
public interface ICommandHandler<TAggregatePayload, TCommand> : ICommandHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload> where TCommand : ICommand<TAggregatePayload>
{
    /// <summary>
    ///     A Command Handler.
    ///     Persistent event can be done using yield return.
    ///     e.g. yield return new Event1();
    ///     Application developer can return multiple event.
    ///     All event will be applied to the one Aggregate type with defined AggregateId.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="context">Command Context has feature to Get Current Aggregate State</param>
    /// <returns></returns>
    public IEnumerable<IEventPayloadApplicableTo<TAggregatePayload>> HandleCommand(TCommand command, ICommandContext<TAggregatePayload> context);
}
/// <summary>
///     Command Handler Interface for ICommand
///     Application developer can implement this interface to define a command handler
///     A Command Handler can receive DI objects through constructor.
///     Handler is async. If no awaiting is required, use
///     <see cref="ICommandHandler{TAggregatePayload,TCommand}" />
/// </summary>
/// <typeparam name="TAggregatePayload">Target Aggregate</typeparam>
/// <typeparam name="TCommand">Target Command</typeparam>
public interface ICommandHandlerAsync<TAggregatePayload, TCommand> : ICommandHandlerCommon<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload> where TCommand : ICommand<TAggregatePayload>
{
    /// <summary>
    ///     A Command Handler.
    ///     Persistent event can be done using yield return.
    ///     e.g. yield return new Event1();
    ///     Application developer can return multiple event.
    ///     All event will be applied to the one Aggregate type with defined AggregateId.
    /// </summary>
    /// <param name="command"></param>
    /// <param name="context">Command Context has feature to Get Current Aggregate State</param>
    /// <returns></returns>
    public IAsyncEnumerable<IEventPayloadApplicableTo<TAggregatePayload>> HandleCommandAsync(
        TCommand command,
        ICommandContext<TAggregatePayload> context);
}
/// <summary>
///     Command Context has feature to Get Current Aggregate State
/// </summary>
/// <typeparam name="TAggregate"></typeparam>
public interface ICommandContext<TAggregate> where TAggregate : IAggregatePayloadCommon
{
    /// <summary>
    ///     Get current Aggregate State
    ///     "Current" meaning if you have already yield return event, this function will return the state after adding yield
    ///     returned events.
    /// </summary>
    /// <returns>Current Aggregate State</returns>
    public AggregateState<TAggregate> GetState();
}
