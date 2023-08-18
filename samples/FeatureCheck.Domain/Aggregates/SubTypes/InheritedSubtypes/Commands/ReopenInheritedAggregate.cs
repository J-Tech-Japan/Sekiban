using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Commands;

public record ReopenInheritedAggregate : ICommand<ClosedSubAggregate>
{
    public string Reason { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<ClosedSubAggregate, ReopenInheritedAggregate>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<ClosedSubAggregate>> HandleCommandAsync(
            Func<AggregateState<ClosedSubAggregate>> getAggregateState,
            ReopenInheritedAggregate command)
        {
            await Task.CompletedTask;
            yield return new InheritedAggregateReopened(command.Reason);
        }
    }
}
