using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;

public record UseLoyaltyPoint(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointUsageTypeKeys Reason,
    int PointAmount,
    string Note
) : ChangeAggregateCommandBase<LoyaltyPoint>(
    ClientId
);

public class UseLoyaltyPointHandler : ChangeAggregateCommandHandlerBase<LoyaltyPoint, UseLoyaltyPoint>
{
    protected override async Task ExecCommandAsync(LoyaltyPoint aggregate, UseLoyaltyPoint command)
    {
        aggregate.UseLoyaltyPoint(
            command.HappenedDate,
            command.Reason,
            command.PointAmount,
            command.Note);
        await Task.CompletedTask;
    }
}
