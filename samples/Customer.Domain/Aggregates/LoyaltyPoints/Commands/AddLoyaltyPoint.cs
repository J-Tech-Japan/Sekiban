using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Customer.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record AddLoyaltyPoint(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointReceiveTypeKeys Reason,
    int PointAmount,
    string Note) : ChangeAggregateCommandBase<LoyaltyPoint>
{
    public AddLoyaltyPoint() : this(Guid.Empty, DateTime.MinValue, LoyaltyPointReceiveTypeKeys.CreditcardUsage, 0, string.Empty) { }
    public override Guid GetAggregateId()
    {
        return ClientId;
    }
}
public class AddLoyaltyPointHandler : ChangeAggregateCommandHandlerBase<LoyaltyPoint, AddLoyaltyPoint>
{
    protected override async IAsyncEnumerable<IChangedEvent<LoyaltyPoint>> ExecCommandAsync(
        AggregateState<LoyaltyPoint> aggregateState,
        AddLoyaltyPoint command)
    {
        await Task.CompletedTask;
        if (aggregateState.Payload.LastOccuredTime is not null && aggregateState.Payload.LastOccuredTime > command.HappenedDate)
        {
            throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
        }
        yield return new LoyaltyPointAdded(command.HappenedDate, command.Reason, command.PointAmount, command.Note);
    }
}