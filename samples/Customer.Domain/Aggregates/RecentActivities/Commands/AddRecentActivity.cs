using Customer.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentActivities.Commands;

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
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public AddRecentActivityHandler(ISekibanDateProducer sekibanDateProducer)
    {
        _sekibanDateProducer = sekibanDateProducer;
    }

    protected override async IAsyncEnumerable<IChangedEvent<RecentActivity>> ExecCommandAsync(
        AggregateState<RecentActivity> aggregate,
        AddRecentActivity command)
    {
        await Task.CompletedTask;
        yield return new RecentActivityAdded(new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
    }
}
