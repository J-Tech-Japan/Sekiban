using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record DeleteLoyaltyPoint(Guid ClientId) : ChangeCommandBase<LoyaltyPoint>, INoValidateCommand
{
    public DeleteLoyaltyPoint() : this(Guid.Empty) { }
    public override Guid GetAggregateId() => ClientId;
    public class Handler : ChangeCommandHandlerBase<LoyaltyPoint, DeleteLoyaltyPoint>
    {
        protected override async IAsyncEnumerable<IApplicableEvent<LoyaltyPoint>> ExecCommandAsync(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            DeleteLoyaltyPoint command)
        {
            await Task.CompletedTask;
            yield return new LoyaltyPointDeleted();
        }
    }
}
