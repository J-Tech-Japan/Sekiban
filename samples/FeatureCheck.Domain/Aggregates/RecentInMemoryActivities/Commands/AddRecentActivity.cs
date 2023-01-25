using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record AddRecentInMemoryActivity
    (Guid RecentInMemoryActivityId, string Activity) : ICommand<RecentInMemoryActivity>
{
    public AddRecentInMemoryActivity() : this(Guid.Empty, string.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => RecentInMemoryActivityId;

    public class Handler : ICommandHandler<RecentInMemoryActivity, AddRecentInMemoryActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public async IAsyncEnumerable<IEventPayloadApplicableTo<RecentInMemoryActivity>> HandleCommandAsync(
            Func<AggregateState<RecentInMemoryActivity>> getAggregateState,
            AddRecentInMemoryActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentInMemoryActivityAdded(
                new RecentInMemoryActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
