using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

/// <summary>
///     Interface for Command for the Tenant and Version Validation
///     Tenant Id will be the root partition key
///     If version validation failed, command execution will throw SekibanCommandInconsistentVersionException
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public interface ITenantCommandWithVersionValidation<TAggregatePayload> : ICommandCommon<TAggregatePayload>,
    IVersionValidationCommandCommon,
    ITenantAggregatePayloadCommon<TAggregatePayload>,
    ITenantCommandCommon
    where TAggregatePayload : IAggregatePayloadCommon, ITenantAggregatePayloadCommon<TAggregatePayload>
{
    string ICommandCommon.GetRootPartitionKey() => GetTenantId();
}
