namespace CustomerDomainContext.Aggregates.Clients.Events;

public record ClientNameChanged : ChangeAggregateEvent<Client>
{
    public string ClientName { get; init; }
    public ClientNameChanged(Guid clientId, string clientName) : base(clientId) =>
        ClientName = clientName;
}
