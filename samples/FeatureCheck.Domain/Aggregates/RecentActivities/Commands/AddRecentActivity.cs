using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Commands;

public record AddRecentActivity(Guid RecentActivityId, string Activity) : ICommand<RecentActivities.RecentActivity>
{
    public AddRecentActivity() : this(Guid.Empty, string.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId()
    {
        return RecentActivityId;
    }

    public class Handler : ICommandHandler<RecentActivities.RecentActivity, AddRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer)
        {
            _sekibanDateProducer = sekibanDateProducer;
        }

        public async IAsyncEnumerable<IEventPayload<RecentActivities.RecentActivity>> HandleCommandAsync(
            Func<AggregateState<RecentActivities.RecentActivity>> getAggregateState,
            AddRecentActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityAdded(
                new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
