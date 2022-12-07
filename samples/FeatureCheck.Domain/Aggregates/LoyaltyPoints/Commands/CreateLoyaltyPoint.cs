using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPoint(Guid ClientId, int InitialPoint) : ICommand<LoyaltyPoint>
{
    public CreateLoyaltyPoint() : this(Guid.Empty, 0)
    {
    }

    public Guid GetAggregateId()
    {
        return ClientId;
    }

    public class Handler : ICommandHandler<LoyaltyPoint, CreateLoyaltyPoint>
    {
        public async IAsyncEnumerable<IEventPayload<LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            CreateLoyaltyPoint command)
        {
            await Task.CompletedTask;
            yield return new LoyaltyPointCreated(command.InitialPoint);
        }
    }
}
