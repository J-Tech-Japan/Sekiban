using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Commands;

public record CloseInheritedAggregate : ICommand<ProcessingSubAggregate>
{
    public string Reason { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }

    public class Handler : ICommandHandler<ProcessingSubAggregate, CloseInheritedAggregate>
    {
        public IEnumerable<IEventPayloadApplicableTo<ProcessingSubAggregate>> HandleCommand(
            CloseInheritedAggregate command,
            ICommandContext<ProcessingSubAggregate> context)
        {
            yield return new InheritedAggregateClosed(command.Reason);
        }
        public Guid SpecifyAggregateId(CloseInheritedAggregate command) => command.AggregateId;
    }
}
