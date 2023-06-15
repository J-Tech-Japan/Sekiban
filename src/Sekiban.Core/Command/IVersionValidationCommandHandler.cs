using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

// ReSharper disable once InvalidXmlDocComment
/// <summary>
///     Command Handler Interface for　<see cref="IVersionValidationCommand" />
///     Application developer can implement this interface to define a command handler
///     A Command Handler can receive DI objects through constructor.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TCommand"></typeparam>
public interface IVersionValidationCommandHandler<TAggregatePayload, TCommand> : ICommandHandler<TAggregatePayload, TCommand>
    where TAggregatePayload : IAggregatePayloadCommon where TCommand : IVersionValidationCommand<TAggregatePayload>
{
}
