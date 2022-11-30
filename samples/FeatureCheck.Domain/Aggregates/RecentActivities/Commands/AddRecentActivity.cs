using Customer.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentActivities.Commands;

public record AddRecentActivity(Guid RecentActivityId, string Activity) : ICommandBase<RecentActivities.RecentActivity>
{
    public AddRecentActivity() : this(Guid.Empty, string.Empty) { }
    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => RecentActivityId;
    public class Handler : ICommandHandlerBase<RecentActivities.RecentActivity, AddRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;
        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public async IAsyncEnumerable<IEventPayload<RecentActivities.RecentActivity>> HandleCommandAsync(
            Func<AggregateState<RecentActivities.RecentActivity>> getAggregateState,
            AddRecentActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityAdded(new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
