using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record AddRecentInMemoryActivity(Guid RecentInMemoryActivityId, string Activity)
    : ICommand<RecentInMemoryActivity>
{

    public int ReferenceVersion { get; init; }
    public AddRecentInMemoryActivity() : this(Guid.Empty, string.Empty)
    {
    }

    public Guid GetAggregateId() => RecentInMemoryActivityId;

    public class Handler : ICommandHandler<RecentInMemoryActivity, AddRecentInMemoryActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public IEnumerable<IEventPayloadApplicableTo<RecentInMemoryActivity>> HandleCommand(
            AddRecentInMemoryActivity command,
            ICommandContext<RecentInMemoryActivity> context)
        {
            yield return new RecentInMemoryActivityAdded(
                new RecentInMemoryActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
