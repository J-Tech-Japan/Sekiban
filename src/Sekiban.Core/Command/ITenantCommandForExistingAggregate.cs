using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for Command for the Tenant and Existing Aggregate
///     Tenant Id will be the root partition key
///     If aggregate does not exist, command execution will throw SekibanAggregateNotFoundException
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public interface ITenantCommandForExistingAggregate<TAggregatePayload> : ICommand<TAggregatePayload>,
    ITenantCommandCommon,
    IAggregateShouldExistCommand where TAggregatePayload : IAggregatePayloadCommon;
