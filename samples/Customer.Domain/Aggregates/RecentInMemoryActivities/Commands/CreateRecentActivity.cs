using Customer.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record CreateRecentInMemoryActivity : ICreateCommand<RecentInMemoryActivity>
{
    public Guid GetAggregateId() => Guid.NewGuid();
}
public class CreateRecentInMemoryActivityHandler : CreateCommandHandlerBase<RecentInMemoryActivity, CreateRecentInMemoryActivity>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;
    public CreateRecentInMemoryActivityHandler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

    protected override async IAsyncEnumerable<IApplicableEvent<RecentInMemoryActivity>> ExecCreateCommandAsync(
        Func<AggregateIdentifierState<RecentInMemoryActivity>> getAggregateState,
        CreateRecentInMemoryActivity command)
    {
        await Task.CompletedTask;
        yield return new RecentInMemoryActivityCreated(new RecentInMemoryActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
    }
}
