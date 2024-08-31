using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPoint(Guid ClientId, int InitialPoint) : ICommand<LoyaltyPoint>
{
    public CreateLoyaltyPoint() : this(Guid.Empty, 0)
    {
    }

    public class Handler : ICommandHandler<LoyaltyPoint, CreateLoyaltyPoint>
    {
        public IEnumerable<IEventPayloadApplicableTo<LoyaltyPoint>> HandleCommand(
            CreateLoyaltyPoint command,
            ICommandContext<LoyaltyPoint> context)
        {
            yield return new LoyaltyPointCreated(command.InitialPoint);
        }
        public Guid SpecifyAggregateId(CreateLoyaltyPoint command) => command.ClientId;
    }
}
