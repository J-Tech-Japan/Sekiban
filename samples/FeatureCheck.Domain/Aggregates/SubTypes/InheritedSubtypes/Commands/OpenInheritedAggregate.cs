using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Commands;

public record OpenInheritedAggregate : ICommand<ProcessingSubAggregate>
{
    [Range(202001, 203012)]
    public int YearMonth { get; init; }
    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<ProcessingSubAggregate, OpenInheritedAggregate>
    {
        public IEnumerable<IEventPayloadApplicableTo<ProcessingSubAggregate>> HandleCommand(
            OpenInheritedAggregate command,
            ICommandContext<ProcessingSubAggregate> context)
        {
            yield return new InheritedAggregateOpened(command.YearMonth);
        }
    }
}
