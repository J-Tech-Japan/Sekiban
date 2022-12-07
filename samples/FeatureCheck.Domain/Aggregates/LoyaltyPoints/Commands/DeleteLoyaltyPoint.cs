using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;

public record DeleteLoyaltyPoint(Guid ClientId) : ICommand<LoyaltyPoint>
{
    public DeleteLoyaltyPoint() : this(Guid.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId()
    {
        return ClientId;
    }

    public class Handler : ICommandHandler<LoyaltyPoint, DeleteLoyaltyPoint>
    {
        public async IAsyncEnumerable<IEventPayload<LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            DeleteLoyaltyPoint command)
        {
            await Task.CompletedTask;
            yield return new LoyaltyPointDeleted();
        }
    }
}
