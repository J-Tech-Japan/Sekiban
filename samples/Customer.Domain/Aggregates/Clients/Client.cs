using Sekiban.Core.Aggregate;
namespace Customer.Domain.Aggregates.Clients;

public record Client(Guid BranchId, string ClientName, string ClientEmail, bool IsDeleted = false) : IDeletableAggregatePayload
{
    public Client() : this(Guid.Empty, string.Empty, string.Empty)
    {
    }
}