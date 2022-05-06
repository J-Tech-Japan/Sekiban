namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;

public record CreateLoyaltyPoint(Guid ClientId, int InitialPoint) : ICreateAggregateCommand<LoyaltyPoint>;
public class CreateLoyaltyPointHandler : CreateAggregateCommandHandlerBase<LoyaltyPoint, CreateLoyaltyPoint>
{
    protected override async Task<LoyaltyPoint> CreateAggregateAsync(CreateLoyaltyPoint command)
    {
        await Task.CompletedTask;
        return new LoyaltyPoint(command.ClientId, command.InitialPoint);
    }
}
