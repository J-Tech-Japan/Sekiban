using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for Command for the Tenant and Version Validation and Existing Aggregate
///     Tenant Id will be the root partition key
///     If aggregate does not exist, command execution will throw SekibanAggregateNotFoundException
///     If version validation failed, command execution will throw SekibanCommandInconsistentVersionException
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public interface
    ITenantCommandWithVersionValidationForExistingAggregate<TAggregatePayload> :
    ICommandWithVersionValidation<TAggregatePayload>,
    ITenantCommandCommon,
    IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon;
