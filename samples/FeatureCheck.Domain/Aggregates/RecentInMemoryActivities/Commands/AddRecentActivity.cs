using Customer.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record AddRecentInMemoryActivity(Guid RecentInMemoryActivityId, string Activity) : ICommand<RecentInMemoryActivities.RecentInMemoryActivity>
{
    public AddRecentInMemoryActivity() : this(Guid.Empty, string.Empty) { }
    public int ReferenceVersion { get; init; }
    public Guid GetAggregateId() => RecentInMemoryActivityId;
    public class Handler : ICommandHandlerBase<RecentInMemoryActivities.RecentInMemoryActivity, AddRecentInMemoryActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;
        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;
        public async IAsyncEnumerable<IEventPayload<RecentInMemoryActivities.RecentInMemoryActivity>> HandleCommandAsync(
            Func<AggregateState<RecentInMemoryActivities.RecentInMemoryActivity>> getAggregateState,
            AddRecentInMemoryActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentInMemoryActivityAdded(new RecentInMemoryActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
