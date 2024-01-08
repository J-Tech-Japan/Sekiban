using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using FeatureCheck.Domain.Shared.Exceptions;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;

public record UseLoyaltyPoint(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointUsageTypeKeys Reason,
    int PointAmount,
    string Note) : IVersionValidationCommand<LoyaltyPoint>
{
    public UseLoyaltyPoint() : this(Guid.Empty, DateTime.MinValue, LoyaltyPointUsageTypeKeys.FlightDomestic, 0, string.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => ClientId;

    public class Handler : IVersionValidationCommandHandler<LoyaltyPoint, UseLoyaltyPoint>
    {
        public IEnumerable<IEventPayloadApplicableTo<LoyaltyPoint>> HandleCommand(UseLoyaltyPoint command, ICommandContext<LoyaltyPoint> context)
        {
            if (context.GetState().Payload.LastOccuredTime > command.HappenedDate)
            {
                throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
            }
            yield return context.GetState().Payload.CurrentPoint - command.PointAmount < 0
                ? throw new SekibanLoyaltyPointNotEnoughException()
                : (IEventPayloadApplicableTo<LoyaltyPoint>)new LoyaltyPointUsed(command.HappenedDate, command.Reason, command.PointAmount, command.Note);
        }
    }
}
