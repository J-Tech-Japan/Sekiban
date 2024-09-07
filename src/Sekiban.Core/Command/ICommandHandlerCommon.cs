using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
namespace Sekiban.Core.Command;

/// <summary>
///     System use for common command handler
///     Application developer do not need to use this class
/// </summary>
/// <typeparam name="TAggregate">Target Aggregate</typeparam>
/// <typeparam name="TCommand">Target command</typeparam>
// ReSharper disable once UnusedTypeParameter
public interface ICommandHandlerCommon<TAggregate, in TCommand> : ICommandHandlerCommonBase
    where TAggregate : IAggregatePayloadCommon where TCommand : ICommandCommon<TAggregate>
{
    public Guid SpecifyAggregateId(TCommand command);

    /// <summary>
    ///     Set root partition key for the command.
    /// </summary>
    /// <returns>root partition key</returns>
    public virtual static string GetRootPartitionKey(TCommand command) => IDocument.DefaultRootPartitionKey;
}
