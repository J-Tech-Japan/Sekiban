using Customer.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentActivities.Commands;

public record CreateRecentActivity : ICreateAggregateCommand<RecentActivity>
{
    public Guid GetAggregateId()
    {
        return Guid.NewGuid();
    }
}
public class CreateRecentActivityHandler : CreateAggregateCommandHandlerBase<RecentActivity, CreateRecentActivity>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public CreateRecentActivityHandler(ISekibanDateProducer sekibanDateProducer)
    {
        _sekibanDateProducer = sekibanDateProducer;
    }

    protected override async IAsyncEnumerable<IApplicableEvent<RecentActivity>> ExecCreateCommandAsync(
        AggregateState<RecentActivity> aggregate,
        CreateRecentActivity command)
    {
        await Task.CompletedTask;
        yield return new RecentActivityCreated(new RecentActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
    }
}
