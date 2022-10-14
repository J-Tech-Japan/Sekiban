namespace CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPoint(Guid ClientId, int InitialPoint) : ICreateAggregateCommand<LoyaltyPoint>
{
    public CreateLoyaltyPoint() : this(Guid.Empty, 0) { }
}
public class CreateLoyaltyPointHandler : CreateAggregateCommandHandlerBase<LoyaltyPoint, CreateLoyaltyPoint>
{

    public override Guid GenerateAggregateId(CreateLoyaltyPoint command)
    {
        return command.ClientId;
    }
    protected override async Task ExecCreateCommandAsync(LoyaltyPoint aggregate, CreateLoyaltyPoint command)
    {
        await Task.CompletedTask;
        aggregate.CreateLoyaltyPoint(command.InitialPoint);
    }
}
