using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;

public record OldV1Command : ICommand<VersionCheckAggregate>
{
    public Guid AggregateId { get; init; }
    public int Amount { get; init; }
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<VersionCheckAggregate, OldV1Command>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<VersionCheckAggregate>> HandleCommandAsync(
            Func<AggregateState<VersionCheckAggregate>> getAggregateState,
            OldV1Command command)
        {
            await Task.CompletedTask;
            yield return new PaymentAdded_V1(command.Amount);
        }
    }
}
