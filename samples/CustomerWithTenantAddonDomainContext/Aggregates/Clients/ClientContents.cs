using Sekiban.Core.Aggregate;
namespace CustomerWithTenantAddonDomainContext.Aggregates.Clients;

public record ClientContents : IAggregateContents
{
    public Guid BranchId { get; init; }
    public string ClientName { get; init; } = default!;
    public string ClientEmail { get; init; } = default!;

    public ClientContents() { }

    public ClientContents(Guid branchId, string clientName, string clientEmail)
    {
        BranchId = branchId;
        ClientName = clientName;
        ClientEmail = clientEmail;
    }
}
