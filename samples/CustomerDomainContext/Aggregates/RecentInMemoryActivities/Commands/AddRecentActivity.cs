namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities.Commands;

public record AddRecentInMemoryActivity(Guid RecentInMemoryActivityId, string Activity) : ChangeAggregateCommandBase<RecentInMemoryActivity>,
    INoValidateCommand
{
    public AddRecentInMemoryActivity() : this(Guid.Empty, string.Empty) { }
    public override Guid GetAggregateId() =>
        RecentInMemoryActivityId;
}
public class AddRecentInMemoryActivityHandler : ChangeAggregateCommandHandlerBase<RecentInMemoryActivity, AddRecentInMemoryActivity>
{
    protected override async Task ExecCommandAsync(RecentInMemoryActivity aggregate, AddRecentInMemoryActivity command)
    {
        aggregate.AddActivity(command.Activity);
        await Task.CompletedTask;
    }
}
