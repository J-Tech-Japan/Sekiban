using FeatureCheck.Domain.Aggregates.ALotOfEvents.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.ALotOfEvents.Commands;

public record ALotOfEventsCreateCommand : ICommand<ALotOfEventsAggregate>
{
    public Guid AggregateId { get; init; }
    public int NumberOfEvents { get; init; }
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<ALotOfEventsAggregate, ALotOfEventsCreateCommand>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<ALotOfEventsAggregate>> HandleCommandAsync(
            Func<AggregateState<ALotOfEventsAggregate>> getAggregateState,
            ALotOfEventsCreateCommand command)
        {
            await Task.CompletedTask;
            foreach (var i in Enumerable.Range(0, command.NumberOfEvents))
            {
                yield return new ALotOfEventsSingleEvent(i.ToString());
            }
        }
    }
}
