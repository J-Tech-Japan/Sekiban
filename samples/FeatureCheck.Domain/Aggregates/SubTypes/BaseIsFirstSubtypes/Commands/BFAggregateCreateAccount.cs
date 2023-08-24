using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;

public record BFAggregateCreateAccount(string Name, long Price) : ICommand<BaseFirstAggregate>
{
    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<BaseFirstAggregate, BFAggregateCreateAccount>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<BaseFirstAggregate>> HandleCommandAsync(
            Func<AggregateState<BaseFirstAggregate>> getAggregateState,
            BFAggregateCreateAccount command)
        {
            await Task.CompletedTask;
            yield return new BFAggregateInitiated(command.Name, command.Price);
        }
    }
}
