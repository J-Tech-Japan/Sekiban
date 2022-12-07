using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record CreateRecentInMemoryActivity : ICommand<RecentInMemoryActivity>
{
    public Guid GetAggregateId()
    {
        return Guid.NewGuid();
    }

    public class Handler : ICommandHandler<RecentInMemoryActivity, CreateRecentInMemoryActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer)
        {
            _sekibanDateProducer = sekibanDateProducer;
        }

        public async IAsyncEnumerable<IEventPayload<RecentInMemoryActivity>> HandleCommandAsync(
            Func<AggregateState<RecentInMemoryActivity>> getAggregateState,
            CreateRecentInMemoryActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentInMemoryActivityCreated(
                new RecentInMemoryActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
        }
    }
}
