using Customer.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentActivities.Commands;

public record AddRecentActivity(Guid RecentActivityId, string Activity) : ChangeCommandBase<RecentActivity>, INoValidateCommand
{
    public AddRecentActivity() : this(Guid.Empty, string.Empty) { }
    public override Guid GetAggregateId() => RecentActivityId;
    public class Handler : ChangeCommandHandlerBase<RecentActivity, AddRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;
        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        protected override async IAsyncEnumerable<IApplicableEvent<RecentActivity>> ExecCommandAsync(
            Func<AggregateState<RecentActivity>> getAggregateState,
            AddRecentActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityAdded(new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
