using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPoint(Guid ClientId, int InitialPoint) : ICommand<LoyaltyPoint>
{
    public CreateLoyaltyPoint() : this(Guid.Empty, 0)
    {
    }

    public Guid GetAggregateId() => ClientId;

    public class Handler : ICommandHandler<LoyaltyPoint, CreateLoyaltyPoint>
    {
        public IEnumerable<IEventPayloadApplicableTo<LoyaltyPoint>> HandleCommand(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            CreateLoyaltyPoint command)
        {
            yield return new LoyaltyPointCreated(command.InitialPoint);
        }
    }
}
