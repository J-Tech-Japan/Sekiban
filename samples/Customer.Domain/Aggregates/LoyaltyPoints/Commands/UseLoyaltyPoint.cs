using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Customer.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record UseLoyaltyPoint(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointUsageTypeKeys Reason,
    int PointAmount,
    string Note) : ChangeCommandBase<LoyaltyPoint>
{
    public UseLoyaltyPoint() : this(Guid.Empty, DateTime.MinValue, LoyaltyPointUsageTypeKeys.FlightDomestic, 0, string.Empty) { }
    public override Guid GetAggregateId() => ClientId;
}
public class UseLoyaltyPointHandler : ChangeCommandHandlerBase<LoyaltyPoint, UseLoyaltyPoint>
{
    protected override async IAsyncEnumerable<IChangedEvent<LoyaltyPoint>> ExecCommandAsync(
        Func<AggregateIdentifierState<LoyaltyPoint>> getAggregateState,
        UseLoyaltyPoint command)
    {
        await Task.CompletedTask;
        if (getAggregateState().Payload.LastOccuredTime > command.HappenedDate)
        {
            throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
        }
        if (getAggregateState().Payload.CurrentPoint - command.PointAmount < 0)
        {
            throw new SekibanLoyaltyPointNotEnoughException();
        }
        yield return new LoyaltyPointUsed(command.HappenedDate, command.Reason, command.PointAmount, command.Note);
    }
}
