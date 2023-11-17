using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Handler Interface for　<see cref="IVersionValidationCommand{TAggregatePayload}" />
///     Application developer can implement this interface to define a command handler
///     A Command Handler can receive DI objects through constructor.
///     Handler is async. If no awaiting is required, use
///     <see cref="IVersionValidationCommandHandler{TAggregatePayload,TCommand}" />
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TCommand"></typeparam>
public interface IVersionValidationCommandHandlerAsync<TAggregatePayload, TCommand> : ICommandHandlerAsync<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload> where TCommand : IVersionValidationCommand<TAggregatePayload>;