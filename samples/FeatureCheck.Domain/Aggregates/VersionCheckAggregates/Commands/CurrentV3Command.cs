using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Enums;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;

public record CurrentV3Command : ICommand<VersionCheckAggregate>
{
    public Guid AggregateId { get; init; }
    public int Amount { get; init; }
    public PaymentKind PaymentKind { get; init; }
    public string Description { get; init; } = string.Empty;
    public Guid GetAggregateId() => AggregateId;

    public class Handler : ICommandHandler<VersionCheckAggregate, CurrentV3Command>
    {
        public async IAsyncEnumerable<IEventPayload<VersionCheckAggregate>> HandleCommandAsync(
            Func<AggregateState<VersionCheckAggregate>> getAggregateState,
            CurrentV3Command command)
        {
            await Task.CompletedTask;
            yield return new PaymentAdded_V3(command.Amount, command.PaymentKind, command.Description);
        }
    }
}