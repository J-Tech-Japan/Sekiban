namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities.Commands;

public record CreateRecentInMemoryActivity(Guid AggregateId) : ICreateAggregateCommand<RecentInMemoryActivity>;

public class CreateRecentInMemoryActivityHandler : CreateAggregateCommandHandlerBase<RecentInMemoryActivity, CreateRecentInMemoryActivity>
{
    protected override async Task<RecentInMemoryActivity> CreateAggregateAsync(CreateRecentInMemoryActivity command)
    {
        await Task.CompletedTask;
        return new(command.AggregateId, "First Event Created");
    }
}
