using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record DeleteLoyaltyPoint(Guid ClientId) : ChangeCommandBase<LoyaltyPoints.LoyaltyPoint>, INoValidateCommand
{
    public DeleteLoyaltyPoint() : this(Guid.Empty) { }
    public override Guid GetAggregateId() => ClientId;
    public class Handler : ChangeCommandHandlerBase<LoyaltyPoints.LoyaltyPoint, DeleteLoyaltyPoint>
    {
        protected override async IAsyncEnumerable<IApplicableEvent<LoyaltyPoints.LoyaltyPoint>> ExecCommandAsync(
            Func<AggregateState<LoyaltyPoints.LoyaltyPoint>> getAggregateState,
            DeleteLoyaltyPoint command)
        {
            await Task.CompletedTask;
            yield return new LoyaltyPointDeleted();
        }
    }
}
