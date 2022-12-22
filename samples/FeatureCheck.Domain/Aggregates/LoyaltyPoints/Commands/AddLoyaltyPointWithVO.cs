using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.ValueObjects;
using FeatureCheck.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;

public record AddLoyaltyPointWithVO : IVersionValidationCommand<LoyaltyPoint>
{
    public Guid ClientId { get; init; } = Guid.Empty;
    public LoyaltyPointReceiveType Reason { get; init; } = new(LoyaltyPointReceiveTypeKeys.CreditcardUsage);
    public LoyaltyPointValue LoyaltyPointValue { get; init; } = new(0);
    public DateTime HappenedDate { get; init; } = SekibanDateProducer.GetRegistered().UtcNow;
    public string Note { get; init; } = string.Empty;
    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;

    public class Handler : IVersionValidationCommandHandler<LoyaltyPoint, AddLoyaltyPointWithVO>
    {

        public async IAsyncEnumerable<IEventPayload<LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            AddLoyaltyPointWithVO command)
        {
            await Task.CompletedTask;
            if (getAggregateState().Payload.LastOccuredTime is not null &&
                getAggregateState().Payload.LastOccuredTime > command.HappenedDate)
            {
                throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
            }
            yield return new LoyaltyPointAdded(command.HappenedDate, command.Reason, command.LoyaltyPointValue, command.Note);
        }
    }
}
