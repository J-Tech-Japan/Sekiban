using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;

public record BFAggregateCreateAccount(string Name, long Price) : ICommand<BaseFirstAggregate>
{
    public class Handler : ICommandHandler<BaseFirstAggregate, BFAggregateCreateAccount>
    {
        public IEnumerable<IEventPayloadApplicableTo<BaseFirstAggregate>> HandleCommand(
            BFAggregateCreateAccount command,
            ICommandContext<BaseFirstAggregate> context)
        {
            yield return new BFAggregateInitiated(command.Name, command.Price);
        }
        public Guid SpecifyAggregateId(BFAggregateCreateAccount command) => Guid.NewGuid();
    }
}
