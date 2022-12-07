using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using FeatureCheck.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;

public record UseLoyaltyPoint(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointUsageTypeKeys Reason,
    int PointAmount,
    string Note) : IVersionValidationCommand<LoyaltyPoint>
{
    public UseLoyaltyPoint() : this(Guid.Empty, DateTime.MinValue, LoyaltyPointUsageTypeKeys.FlightDomestic, 0,
        string.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId()
    {
        return ClientId;
    }

    public class Handler : IVersionValidationCommandHandler<LoyaltyPoint, UseLoyaltyPoint>
    {
        public async IAsyncEnumerable<IEventPayload<LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoint>> getAggregateState,
            UseLoyaltyPoint command)
        {
            await Task.CompletedTask;
            if (getAggregateState().Payload.LastOccuredTime > command.HappenedDate)
                throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
            if (getAggregateState().Payload.CurrentPoint - command.PointAmount < 0)
                throw new SekibanLoyaltyPointNotEnoughException();
            yield return new LoyaltyPointUsed(command.HappenedDate, command.Reason, command.PointAmount, command.Note);
        }
    }
}
