using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Commands;

public record CloseInheritedAggregate : ICommand<ProcessingSubAggregate>
{
    public string Reason { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<ProcessingSubAggregate, CloseInheritedAggregate>
    {
        public IEnumerable<IEventPayloadApplicableTo<ProcessingSubAggregate>> HandleCommand(
            Func<AggregateState<ProcessingSubAggregate>> getAggregateState,
            CloseInheritedAggregate command)
        {
            yield return new InheritedAggregateClosed(command.Reason);
        }
    }
}
