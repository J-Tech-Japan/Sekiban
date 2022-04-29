namespace CustomerDomainContext.Aggregates.Clients;

public record ClientDto : AggregateDtoBase
{
    public Guid BranchId { get; init; } = Guid.Empty;
    public string ClientName { get; init; } = null!;
    public string ClientEmail { get; init; } = null!;

    public ClientDto() { }
    public ClientDto(Client aggregate) : base(aggregate) { }
}
