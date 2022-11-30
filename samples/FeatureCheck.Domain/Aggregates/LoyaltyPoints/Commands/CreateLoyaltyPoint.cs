using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPoint(Guid ClientId, int InitialPoint) : ICommandBase<LoyaltyPoints.LoyaltyPoint>
{
    public CreateLoyaltyPoint() : this(Guid.Empty, 0) { }
    public Guid GetAggregateId() => ClientId;
    public class Handler : ICommandHandlerBase<LoyaltyPoints.LoyaltyPoint, CreateLoyaltyPoint>
    {
        public async IAsyncEnumerable<IEventPayload<LoyaltyPoints.LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoints.LoyaltyPoint>> getAggregateState,
            CreateLoyaltyPoint command)
        {
            await Task.CompletedTask;
            yield return new LoyaltyPointCreated(command.InitialPoint);
        }
    }
}
