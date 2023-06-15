using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     System use for common command handler
///     Application developer do not need to use this class
/// </summary>
/// <typeparam name="TAggregate">Target Aggregate</typeparam>
/// <typeparam name="TCommand">Target command</typeparam>
// ReSharper disable once UnusedTypeParameter
public interface ICommandHandlerCommon<TAggregate, TCommand> : ICommandHandlerCommonBase
    where TAggregate : IAggregatePayloadCommon where TCommand : ICommand<TAggregate>
{
}
