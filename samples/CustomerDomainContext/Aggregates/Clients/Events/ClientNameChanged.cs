namespace CustomerDomainContext.Aggregates.Clients.Events;

public record ClientNameChanged : IChangedEventPayload
{
    public string ClientName { get; init; }
    public ClientNameChanged(string clientName) =>
        ClientName = clientName;
}
