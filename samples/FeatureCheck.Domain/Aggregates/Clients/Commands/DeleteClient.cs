using FeatureCheck.Domain.Aggregates.Clients.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Clients.Commands;

public record DeleteClient(Guid ClientId) : IVersionValidationCommand<Client>
{
    public DeleteClient() : this(Guid.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;

    public class Handler : IVersionValidationCommandHandler<Client, DeleteClient>
    {
        public IEnumerable<IEventPayloadApplicableTo<Client>> HandleCommand(Func<AggregateState<Client>> getAggregateStateState, DeleteClient command)
        {
            yield return new ClientDeleted();
        }
    }
}
