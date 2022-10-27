using Customer.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record CreateRecentInMemoryActivity : ICreateAggregateCommand<RecentInMemoryActivity>
{
    public Guid GetAggregateId()
    {
        return Guid.NewGuid();
    }
}
public class CreateRecentInMemoryActivityHandler : CreateAggregateCommandHandlerBase<RecentInMemoryActivity, CreateRecentInMemoryActivity>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public CreateRecentInMemoryActivityHandler(ISekibanDateProducer sekibanDateProducer)
    {
        _sekibanDateProducer = sekibanDateProducer;
    }

    protected override async IAsyncEnumerable<IApplicableEvent<RecentInMemoryActivity>> ExecCreateCommandAsync(
        AggregateState<RecentInMemoryActivity> aggregateState,
        CreateRecentInMemoryActivity command)
    {
        await Task.CompletedTask;
        yield return new RecentInMemoryActivityCreated(new RecentInMemoryActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
    }
}
