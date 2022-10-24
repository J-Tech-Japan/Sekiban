using Sekiban.Core.Aggregate;
namespace Customer.Domain.Aggregates.Clients;

public record ClientPayload : IAggregatePayload
{
    public Guid BranchId { get; init; }
    public string ClientName { get; init; } = default!;
    public string ClientEmail { get; init; } = default!;

    public ClientPayload()
    {
    }

    public ClientPayload(Guid branchId, string clientName, string clientEmail)
    {
        BranchId = branchId;
        ClientName = clientName;
        ClientEmail = clientEmail;
    }
}
