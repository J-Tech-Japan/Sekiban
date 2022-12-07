using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Commands;

public record CreateRecentActivity : ICommand<RecentActivities.RecentActivity>
{
    public Guid GetAggregateId()
    {
        return Guid.NewGuid();
    }

    public class Handler : ICommandHandler<RecentActivities.RecentActivity, CreateRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer)
        {
            _sekibanDateProducer = sekibanDateProducer;
        }

        public async IAsyncEnumerable<IEventPayload<RecentActivities.RecentActivity>> HandleCommandAsync(
            Func<AggregateState<RecentActivities.RecentActivity>> getAggregateState,
            CreateRecentActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityCreated(new RecentActivityRecord("First Event Created",
                _sekibanDateProducer.UtcNow));
        }
    }
}
