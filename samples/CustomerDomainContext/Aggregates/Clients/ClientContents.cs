namespace CustomerDomainContext.Aggregates.Clients;

public record ClientContents : IAggregateContents
{
    public Guid BranchId { get; init; }
    public string ClientName { get; init; }
    public string ClientEmail { get; init; }
    public ClientContents() { }
    public ClientContents(Guid branchId, string clientName, string clientEmail)
    {
        BranchId = branchId;
        ClientName = clientName;
        ClientEmail = clientEmail;
    }
}
