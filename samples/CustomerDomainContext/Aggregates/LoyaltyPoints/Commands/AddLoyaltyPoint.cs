using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;

public record AddLoyaltyPoint(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointReceiveTypeKeys Reason,
    int PointAmount,
    string Note) : ChangeAggregateCommandBase<LoyaltyPoint>(ClientId);
public class AddLoyaltyPointHandler : ChangeAggregateCommandHandlerBase<LoyaltyPoint, AddLoyaltyPoint>
{
    protected override async Task ExecCommandAsync(LoyaltyPoint aggregate, AddLoyaltyPoint command)
    {
        aggregate.AddLoyaltyPoint(command.HappenedDate, command.Reason, command.PointAmount, command.Note);
        await Task.CompletedTask;
    }
}