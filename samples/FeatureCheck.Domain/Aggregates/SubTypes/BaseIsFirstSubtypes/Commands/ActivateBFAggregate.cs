using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;

public record ActivateBFAggregate(Guid BfAggregateId) : ICommand<BaseFirstAggregate>
{
    public Guid GetAggregateId() => BfAggregateId;

    public class Handler : ICommandHandler<BaseFirstAggregate, ActivateBFAggregate>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<BaseFirstAggregate>> HandleCommandAsync(
            Func<AggregateState<BaseFirstAggregate>> getAggregateState,
            ActivateBFAggregate command)
        {
            await Task.CompletedTask;
            yield return new BFAggregateActivated();
        }
    }
}
