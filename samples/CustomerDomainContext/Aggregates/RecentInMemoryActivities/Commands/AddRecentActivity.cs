namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities.Commands;

public record AddRecentInMemoryActivity(
    Guid AggregateId,
    string Activity
) : ChangeAggregateCommandBase<RecentInMemoryActivity>(
    AggregateId
) , INoValidateCommand;

public class AddRecentInMemoryActivityHandler : ChangeAggregateCommandHandlerBase<RecentInMemoryActivity, AddRecentInMemoryActivity>
{
    protected override async Task ExecCommandAsync(RecentInMemoryActivity aggregate, AddRecentInMemoryActivity command)
    {
        aggregate.AddActivity(command.Activity);
        await Task.CompletedTask;
    }
}
