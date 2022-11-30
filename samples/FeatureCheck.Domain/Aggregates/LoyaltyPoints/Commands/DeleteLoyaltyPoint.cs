using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record DeleteLoyaltyPoint(Guid ClientId) : ICommand<LoyaltyPoints.LoyaltyPoint>
{
    public DeleteLoyaltyPoint() : this(Guid.Empty) { }
    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;
    public class Handler : ICommandHandlerBase<LoyaltyPoints.LoyaltyPoint, DeleteLoyaltyPoint>
    {
        public async IAsyncEnumerable<IEventPayload<LoyaltyPoints.LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoints.LoyaltyPoint>> getAggregateState,
            DeleteLoyaltyPoint command)
        {
            await Task.CompletedTask;
            yield return new LoyaltyPointDeleted();
        }
    }
}
