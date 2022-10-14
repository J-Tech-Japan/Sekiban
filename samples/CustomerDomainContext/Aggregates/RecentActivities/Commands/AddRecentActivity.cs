namespace CustomerDomainContext.Aggregates.RecentActivities.Commands;

public record AddRecentActivity(Guid RecentActivityId, string Activity) : ChangeAggregateCommandBase<RecentActivity>, INoValidateCommand
{
    public AddRecentActivity() : this(Guid.Empty, string.Empty) { }
    public override Guid GetAggregateId()
    {
        return RecentActivityId;
    }
}
public class AddRecentActivityHandler : ChangeAggregateCommandHandlerBase<RecentActivity, AddRecentActivity>
{
    protected override async Task ExecCommandAsync(RecentActivity aggregate, AddRecentActivity command)
    {
        aggregate.AddActivity(command.Activity);
        await Task.CompletedTask;
    }
}
