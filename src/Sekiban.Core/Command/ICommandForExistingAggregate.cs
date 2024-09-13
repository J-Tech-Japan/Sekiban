using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for Command for Existing Aggregate
///     If aggregate does not exist, command execution will throw SekibanAggregateNotFoundException
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public interface ICommandForExistingAggregate<TAggregatePayload> : ICommand<TAggregatePayload>,
    IAggregateShouldExistCommand
    where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>;
