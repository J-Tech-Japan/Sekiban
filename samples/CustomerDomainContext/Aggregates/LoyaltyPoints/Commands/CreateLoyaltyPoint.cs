namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPoint(Guid ClientId, int InitialPoint) : ICreateAggregateCommand<LoyaltyPoint>;
public class CreateLoyaltyPointHandler : CreateAggregateCommandHandlerBase<LoyaltyPoint, CreateLoyaltyPoint>
{

    protected override async Task ExecCreateCommandAsync(LoyaltyPoint aggregate, CreateLoyaltyPoint command)
    {
        await Task.CompletedTask;
        aggregate.CreateLoyaltyPoint(command.InitialPoint);
    }
}
