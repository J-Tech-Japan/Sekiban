using Customer.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record AddRecentInMemoryActivity(Guid RecentInMemoryActivityId, string Activity) : ChangeAggregateCommandBase<RecentInMemoryActivity>,
    INoValidateCommand
{
    public AddRecentInMemoryActivity() : this(Guid.Empty, string.Empty) { }
    public override Guid GetAggregateId()
    {
        return RecentInMemoryActivityId;
    }
}
public class AddRecentInMemoryActivityHandler : ChangeAggregateCommandHandlerBase<RecentInMemoryActivity, AddRecentInMemoryActivity>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public AddRecentInMemoryActivityHandler(ISekibanDateProducer sekibanDateProducer)
    {
        _sekibanDateProducer = sekibanDateProducer;
    }
    protected override async IAsyncEnumerable<IChangedEvent<RecentInMemoryActivity>> ExecCommandAsync(
        AggregateState<RecentInMemoryActivity> aggregateState,
        AddRecentInMemoryActivity command)
    {
        await Task.CompletedTask;
        yield return new RecentInMemoryActivityAdded(new RecentInMemoryActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
    }
}
