using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;

public record CurrentV3Command : ICommand<VersionCheckAggregate>
{
    public Guid AggregateId { get; init; }
    public int Amount { get; init; }
    public PaymentKind PaymentKind { get; init; }
    public string Description { get; init; } = string.Empty;

    public class Handler : ICommandHandler<VersionCheckAggregate, CurrentV3Command>
    {
        public IEnumerable<IEventPayloadApplicableTo<VersionCheckAggregate>> HandleCommand(
            CurrentV3Command command,
            ICommandContext<VersionCheckAggregate> context)
        {
            yield return new PaymentAdded_V3(command.Amount, command.PaymentKind, command.Description);
        }
        public Guid SpecifyAggregateId(CurrentV3Command command) => command.AggregateId;
    }
}
