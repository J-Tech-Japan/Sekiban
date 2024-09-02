using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Commands;

public record AddRecentActivity(Guid RecentActivityId, string Activity) : ICommand<RecentActivity>
{

    public int ReferenceVersion { get; init; }
    public AddRecentActivity() : this(Guid.Empty, string.Empty)
    {
    }

    public class Handler : ICommandHandler<RecentActivity, AddRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public IEnumerable<IEventPayloadApplicableTo<RecentActivity>> HandleCommand(
            AddRecentActivity command,
            ICommandContext<RecentActivity> context)
        {
            yield return new RecentActivityAdded(
                new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
        public Guid SpecifyAggregateId(AddRecentActivity command) => command.RecentActivityId;
    }
}
