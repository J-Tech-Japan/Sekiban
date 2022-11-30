using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record LoyaltyPoint(Guid ClientId, int InitialPoint) : ICommandBase<LoyaltyPoints.LoyaltyPoint>
{
    public LoyaltyPoint() : this(Guid.Empty, 0) { }
    public Guid GetAggregateId() => ClientId;
    public class Handler : ICommandHandlerBase<LoyaltyPoints.LoyaltyPoint, LoyaltyPoint>
    {
        public async IAsyncEnumerable<IApplicableEvent<LoyaltyPoints.LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoints.LoyaltyPoint>> getAggregateState,
            LoyaltyPoint command)
        {
            await Task.CompletedTask;
            yield return new LoyaltyPointCreated(command.InitialPoint);
        }
    }
}
