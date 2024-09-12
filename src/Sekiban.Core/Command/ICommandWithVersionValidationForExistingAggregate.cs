using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for Command with Version Validation for Existing Aggregate
///     If version validation failed, command execution will throw SekibanCommandInconsistentVersionException
///     If aggregate does not exist, command execution will throw SekibanAggregateNotFoundException
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public interface ICommandWithVersionValidationForExistingAggregate<TAggregatePayload> : ICommand<TAggregatePayload>,
    IVersionValidationCommandCommon,
    IAggregateShouldExistCommand
    where TAggregatePayload : IAggregatePayloadCommon, IAggregatePayloadCommon<TAggregatePayload>;
