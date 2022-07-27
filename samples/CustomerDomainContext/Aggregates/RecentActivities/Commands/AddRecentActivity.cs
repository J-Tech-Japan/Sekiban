namespace CustomerDomainContext.Aggregates.RecentActivities.Commands;

public record AddRecentActivity(Guid RecentActivityId, string Activity) : ChangeAggregateCommandBase<RecentActivity>, INoValidateCommand
{
    public override Guid GetAggregateId() =>
        RecentActivityId;
}
public class AddRecentActivityHandler : ChangeAggregateCommandHandlerBase<RecentActivity, AddRecentActivity>
{
    protected override async Task ExecCommandAsync(RecentActivity aggregate, AddRecentActivity command)
    {
        aggregate.AddActivity(command.Activity);
        await Task.CompletedTask;
    }
}
