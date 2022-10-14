using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Consts;
namespace CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Commands;

public record UseLoyaltyPoint(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointUsageTypeKeys Reason,
    int PointAmount,
    string Note) : ChangeAggregateCommandBase<LoyaltyPoint>
{
    public UseLoyaltyPoint() : this(Guid.Empty, DateTime.MinValue, LoyaltyPointUsageTypeKeys.FlightDomestic, 0, string.Empty) { }
    public override Guid GetAggregateId()
    {
        return ClientId;
    }
}
public class UseLoyaltyPointHandler : ChangeAggregateCommandHandlerBase<LoyaltyPoint, UseLoyaltyPoint>
{
    protected override async Task ExecCommandAsync(LoyaltyPoint aggregate, UseLoyaltyPoint command)
    {
        aggregate.UseLoyaltyPoint(command.HappenedDate, command.Reason, command.PointAmount, command.Note);
        await Task.CompletedTask;
    }
}
