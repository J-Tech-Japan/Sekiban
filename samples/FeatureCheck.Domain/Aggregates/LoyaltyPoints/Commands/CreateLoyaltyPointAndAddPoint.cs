using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPointAndAddPoint(Guid ClientId, int AddingPoint) : ICommand<LoyaltyPoint>
{
    public CreateLoyaltyPointAndAddPoint() : this(Guid.Empty, 0)
    {
    }

    public Guid GetAggregateId() => ClientId;

    public class Handler : ICommandHandler<LoyaltyPoint, CreateLoyaltyPointAndAddPoint>
    {
        private readonly ISekibanDateProducer _dateProducer;
        private readonly IAggregateLoader aggregateLoader;

        public Handler(IAggregateLoader aggregateLoader, ISekibanDateProducer dateProducer)
        {
            this.aggregateLoader = aggregateLoader;
            _dateProducer = dateProducer;
        }

        public async IAsyncEnumerable<IEventPayloadApplicableTo<LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            CreateLoyaltyPointAndAddPoint command)
        {
            yield return new LoyaltyPointCreated(0);
            getAggregateState(); // to reproduce the issue;
            yield return new LoyaltyPointAdded(
                _dateProducer.UtcNow,
                LoyaltyPointReceiveTypeKeys.InitialGift,
                command.AddingPoint,
                "Initial User Gift");
            // initial gift for gmail user.
            var client = await aggregateLoader.AsDefaultStateAsync<Client>(getAggregateState().AggregateId);
            if (client != null && client.Payload.ClientEmail.ToLower().EndsWith("@gmail.com"))
            {
                yield return new LoyaltyPointAdded(
                    _dateProducer.UtcNow,
                    LoyaltyPointReceiveTypeKeys.InitialGmailUserGift,
                    command.AddingPoint,
                    "Gmail users gift");

            }
        }
    }
}
