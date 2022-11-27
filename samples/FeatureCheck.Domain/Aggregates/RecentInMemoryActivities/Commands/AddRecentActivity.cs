using Customer.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record AddRecentInMemoryActivity(Guid RecentInMemoryActivityId, string Activity) : ChangeCommandBase<RecentInMemoryActivity>,
    INoValidateCommand
{
    public AddRecentInMemoryActivity() : this(Guid.Empty, string.Empty) { }
    public override Guid GetAggregateId() => RecentInMemoryActivityId;
    public class Handler : ChangeCommandHandlerBase<RecentInMemoryActivity, AddRecentInMemoryActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;
        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;
        protected override async IAsyncEnumerable<IApplicableEvent<RecentInMemoryActivity>> ExecCommandAsync(
            Func<AggregateState<RecentInMemoryActivity>> getAggregateState,
            AddRecentInMemoryActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentInMemoryActivityAdded(new RecentInMemoryActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
