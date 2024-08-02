using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.Clients;

public record Client(Guid BranchId, string ClientName, string ClientEmail, bool IsDeleted = false)
    : IDeletableAggregatePayload<Client>
{
    public static Client CreateInitialPayload(Client? _) => new(Guid.Empty, string.Empty, string.Empty);
}
