namespace CustomerDomainContext.Aggregates.RecentActivities.Commands;

public record CreateRecentActivity(Guid AggregateId) : ICreateAggregateCommand<RecentActivity>;
public class CreateRecentActivityHandler : CreateAggregateCommandHandlerBase<RecentActivity, CreateRecentActivity>
{
    protected override async Task ExecCreateCommandAsync(RecentActivity aggregate, CreateRecentActivity command)
    {
        await Task.CompletedTask;
        aggregate.CreateRecentActivity("First Event Created");
    }
}
