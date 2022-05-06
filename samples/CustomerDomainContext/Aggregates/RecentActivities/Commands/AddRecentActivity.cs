namespace CustomerDomainContext.Aggregates.RecentActivities.Commands;

public record AddRecentActivity(Guid AggregateId, string Activity) : ChangeAggregateCommandBase<RecentActivity>(AggregateId), INoValidateCommand;
public class AddRecentActivityHandler : ChangeAggregateCommandHandlerBase<RecentActivity, AddRecentActivity>
{
    protected override async Task ExecCommandAsync(RecentActivity aggregate, AddRecentActivity command)
    {
        aggregate.AddActivity(command.Activity);
        await Task.CompletedTask;
    }
}
