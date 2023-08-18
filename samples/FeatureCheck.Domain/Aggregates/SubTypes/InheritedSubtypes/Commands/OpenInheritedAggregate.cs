using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Events;
using Sekiban.Core.Aggregate;
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
        public async IAsyncEnumerable<IEventPayloadApplicableTo<ProcessingSubAggregate>> HandleCommandAsync(
            Func<AggregateState<ProcessingSubAggregate>> getAggregateState,
            OpenInheritedAggregate command)
        {
            await Task.CompletedTask;
            yield return new InheritedAggregateOpened(command.YearMonth);
        }
    }
}
