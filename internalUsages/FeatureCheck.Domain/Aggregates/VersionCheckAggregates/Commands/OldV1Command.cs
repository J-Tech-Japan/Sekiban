using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;

public record OldV1Command : ICommand<VersionCheckAggregate>
{
    public Guid AggregateId { get; init; }
    public int Amount { get; init; }

    public class Handler : ICommandHandler<VersionCheckAggregate, OldV1Command>
    {
        public IEnumerable<IEventPayloadApplicableTo<VersionCheckAggregate>> HandleCommand(
            OldV1Command command,
            ICommandContext<VersionCheckAggregate> context)
        {
            yield return new PaymentAdded_V1(command.Amount);
        }
        public Guid SpecifyAggregateId(OldV1Command command) => command.AggregateId;
    }
}
