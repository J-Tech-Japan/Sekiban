namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities.Commands;

public record CreateRecentInMemoryActivity(Guid AggregateId) : ICreateAggregateCommand<RecentInMemoryActivity>;
public class CreateRecentInMemoryActivityHandler : CreateAggregateCommandHandlerBase<RecentInMemoryActivity, CreateRecentInMemoryActivity>
{
    protected override async Task ExecCreateCommandAsync(RecentInMemoryActivity aggregate, CreateRecentInMemoryActivity command)
    {
        await Task.CompletedTask;
        aggregate.CreateRecentInMemoryActivity("First Event Created");
    }
}
