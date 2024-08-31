using FeatureCheck.Domain.Aggregates.ALotOfEvents.Events;
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
        public IEnumerable<IEventPayloadApplicableTo<ALotOfEventsAggregate>> HandleCommand(
            ALotOfEventsCreateCommand command,
            ICommandContext<ALotOfEventsAggregate> context)
        {
            foreach (var i in Enumerable.Range(0, command.NumberOfEvents))
                yield return new ALotOfEventsSingleEvent(i.ToString());
        }
        public Guid SpecifyAggregateId(ALotOfEventsCreateCommand command) => command.AggregateId;
    }
}
