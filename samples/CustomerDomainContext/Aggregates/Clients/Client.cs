using CustomerDomainContext.Aggregates.Clients.Events;
namespace CustomerDomainContext.Aggregates.Clients
{
    public class Client : TransferableAggregateBase<ClientContents>
    {
        public void CreateClient(Guid branchId, NameString clientName, EmailString clientEmail)
        {
            AddAndApplyEvent(new ClientCreated(branchId, clientName, clientEmail));
        }

        protected override Action? GetApplyEventAction(IAggregateEvent ev, IEventPayload payload) =>
            payload switch
            {
                ClientCreated clientChanged => () =>
                {
                    Contents = new ClientContents(clientChanged.BranchId, clientChanged.ClientName, clientChanged.ClientEmail);
                },

                ClientNameChanged clientNameChanged => () => Contents = Contents with { ClientName = clientNameChanged.ClientName },

                ClientDeleted => () => IsDeleted = true,

                _ => null
            };

        public void ChangeClientName(NameString clientName)
        {
            var ev = new ClientNameChanged(clientName);
            // ValueObjectへの代入では行えない集約内の検証をここに記述する。
            AddAndApplyEvent(ev);
        }

        public void Delete() =>
            AddAndApplyEvent(new ClientDeleted());
    }
}
