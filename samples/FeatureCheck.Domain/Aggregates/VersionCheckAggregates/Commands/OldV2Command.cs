using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;

public class OldV2Command : ICommand<VersionCheckAggregate>
{
    public Guid AggregateId { get; init; }
    public int Amount { get; init; }
    public PaymentKind PaymentKind { get; init; }
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<VersionCheckAggregate, OldV2Command>
    {
        public IEnumerable<IEventPayloadApplicableTo<VersionCheckAggregate>> HandleCommand(
            Func<AggregateState<VersionCheckAggregate>> getAggregateState,
            OldV2Command command)
        {
            yield return new PaymentAdded_V2(command.Amount, command.PaymentKind);
        }
    }
}
