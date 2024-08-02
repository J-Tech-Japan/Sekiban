using FeatureCheck.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record ChangeClientNameWithoutLoading(Guid ClientId, string ClientName) : ICommandWithoutLoadingAggregate<Client>
{
    public Guid GetAggregateId() => ClientId;

    public class Handler : ICommandWithoutLoadingAggregateHandler<Client, ChangeClientNameWithoutLoading>
    {
        public IEnumerable<IEventPayloadApplicableTo<Client>> HandleCommand(
            Guid aggregateId,
            ChangeClientNameWithoutLoading command)
        {
            yield return new ClientNameChanged(command.ClientName);
        }
    }
}
