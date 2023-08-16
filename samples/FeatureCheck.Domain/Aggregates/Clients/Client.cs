using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.Clients;

public record Client(Guid BranchId, string ClientName, string ClientEmail, bool IsDeleted = false) : IDeletableAggregatePayload
{
    public static IAggregatePayloadCommon CreateInitialPayload() => new Client(Guid.Empty, string.Empty, string.Empty);
}
