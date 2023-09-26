using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;

public record DeleteLoyaltyPoint(Guid ClientId) : ICommand<LoyaltyPoint>
{

    public int ReferenceVersion { get; init; }
    public DeleteLoyaltyPoint() : this(Guid.Empty)
    {
    }

    public Guid GetAggregateId() => ClientId;

    public class Handler : ICommandHandler<LoyaltyPoint, DeleteLoyaltyPoint>
    {
        public IEnumerable<IEventPayloadApplicableTo<LoyaltyPoint>> HandleCommand(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            DeleteLoyaltyPoint command)
        {
            yield return new LoyaltyPointDeleted();
        }
    }
}
