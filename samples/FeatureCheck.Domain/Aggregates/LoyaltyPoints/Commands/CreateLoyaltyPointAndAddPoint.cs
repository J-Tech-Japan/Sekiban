using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPointAndAddPoint(Guid ClientId, int AddingPoint) : ICreateCommand<LoyaltyPoint>
{
    public CreateLoyaltyPointAndAddPoint() : this(Guid.Empty, 0)
    {
    }
    public Guid GetAggregateId() => ClientId;

    public class Handler : CreateCommandHandlerBase<LoyaltyPoint, CreateLoyaltyPointAndAddPoint>
    {
        private readonly ISekibanDateProducer _dateProducer;
        private readonly IAggregateLoader aggregateLoader;
        public Handler(IAggregateLoader aggregateLoader, ISekibanDateProducer dateProducer)
        {
            this.aggregateLoader = aggregateLoader;
            _dateProducer = dateProducer;
        }
        protected override async IAsyncEnumerable<IApplicableEvent<LoyaltyPoint>> ExecCreateCommandAsync(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            CreateLoyaltyPointAndAddPoint command)
        {
            await aggregateLoader.AsAggregateAsync<Client>(getAggregateState().AggregateId);
            yield return new LoyaltyPointCreated(0);
            getAggregateState(); // to reproduce the issue;
            yield return new LoyaltyPointAdded(_dateProducer.UtcNow, LoyaltyPointReceiveTypeKeys.InitialGift, command.AddingPoint, string.Empty);
        }
    }
}
