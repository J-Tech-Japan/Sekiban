using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPoint(Guid ClientId, int InitialPoint) : ICreateCommand<LoyaltyPoint>
{
    public CreateLoyaltyPoint() : this(Guid.Empty, 0) { }
    public Guid GetAggregateId() => ClientId;
    public class Handler : CreateCommandHandlerBase<LoyaltyPoint, CreateLoyaltyPoint>
    {
        protected override async IAsyncEnumerable<IApplicableEvent<LoyaltyPoint>> ExecCreateCommandAsync(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            CreateLoyaltyPoint command)
        {
            await Task.CompletedTask;
            yield return new LoyaltyPointCreated(command.InitialPoint);
        }
    }
}
