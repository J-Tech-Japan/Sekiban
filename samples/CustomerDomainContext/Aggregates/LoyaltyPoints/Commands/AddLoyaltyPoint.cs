using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
using Sekiban.Core.Command;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;

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
    protected override async Task ExecCommandAsync(LoyaltyPoint aggregate, AddLoyaltyPoint command)
    {
        aggregate.AddLoyaltyPoint(command.HappenedDate, command.Reason, command.PointAmount, command.Note);
        await Task.CompletedTask;
    }
}
