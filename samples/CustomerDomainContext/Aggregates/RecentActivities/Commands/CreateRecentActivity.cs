namespace CustomerDomainContext.Aggregates.RecentActivities.Commands;

public record CreateRecentActivity(Guid AggregateId) : ICreateAggregateCommand<RecentActivity>;
public class CreateRecentActivityHandler : CreateAggregateCommandHandlerBase<RecentActivity, CreateRecentActivity>
{
    protected override async Task<RecentActivity> CreateAggregateAsync(CreateRecentActivity command)
    {
        await Task.CompletedTask;
        return new RecentActivity(command.AggregateId, "First Event Created");
    }
}
