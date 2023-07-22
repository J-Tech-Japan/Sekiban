using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;

public record CloseInheritedAggregate : ICommand<ProcessingSubAggregate>
{
    public string Reason { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<ProcessingSubAggregate, CloseInheritedAggregate>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<ProcessingSubAggregate>> HandleCommandAsync(
            Func<AggregateState<ProcessingSubAggregate>> getAggregateState,
            CloseInheritedAggregate command)
        {
            await Task.CompletedTask;
            yield return new InheritedAggregateClosed(command.Reason);
        }
    }
}
