using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.Branches.Commands;

public record NotAddingAnyEventCommand : ICommand<Branch>
{
    public Guid GetAggregateId() => Guid.Empty;
    public class Handler : ICommandHandler<Branch, NotAddingAnyEventCommand>
    {
        public IEnumerable<IEventPayloadApplicableTo<Branch>> HandleCommand(NotAddingAnyEventCommand command, ICommandContext<Branch> context)
        {
            yield break;
        }
    }
}
