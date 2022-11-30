using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record LoyaltyPointAndAddPoint(Guid ClientId, int AddingPoint) : ICommandBase<LoyaltyPoints.LoyaltyPoint>
{
    public LoyaltyPointAndAddPoint() : this(Guid.Empty, 0)
    {
    }
    public Guid GetAggregateId() => ClientId;

    public class Handler : ICommandHandlerBase<LoyaltyPoints.LoyaltyPoint, LoyaltyPointAndAddPoint>
    {
        private readonly ISekibanDateProducer _dateProducer;
        private readonly IAggregateLoader aggregateLoader;
        public Handler(IAggregateLoader aggregateLoader, ISekibanDateProducer dateProducer)
        {
            this.aggregateLoader = aggregateLoader;
            _dateProducer = dateProducer;
        }
        public async IAsyncEnumerable<IApplicableEvent<LoyaltyPoints.LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoints.LoyaltyPoint>> getAggregateState,
            LoyaltyPointAndAddPoint command)
        {
            await aggregateLoader.AsAggregateAsync<Client>(getAggregateState().AggregateId);
            yield return new LoyaltyPointCreated(0);
            getAggregateState(); // to reproduce the issue;
            yield return new LoyaltyPointAdded(_dateProducer.UtcNow, LoyaltyPointReceiveTypeKeys.InitialGift, command.AddingPoint, string.Empty);
        }
    }
}
