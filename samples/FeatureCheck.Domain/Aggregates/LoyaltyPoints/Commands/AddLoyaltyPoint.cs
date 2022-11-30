using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Customer.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record AddLoyaltyPoint(
    Guid ClientId,
    DateTime HappenedDate,
    LoyaltyPointReceiveTypeKeys Reason,
    int PointAmount,
    string Note) : IVersionValidationCommand<LoyaltyPoints.LoyaltyPoint>
{
    public AddLoyaltyPoint() : this(Guid.Empty, DateTime.MinValue, LoyaltyPointReceiveTypeKeys.CreditcardUsage, 0, string.Empty) { }
    public int ReferenceVersion { get; init; }
    public Guid GetAggregateId() => ClientId;
    public class Handler : IVersionValidationCommandHandlerBase<LoyaltyPoints.LoyaltyPoint, AddLoyaltyPoint>
    {
        public async IAsyncEnumerable<IEventPayload<LoyaltyPoints.LoyaltyPoint>> HandleCommandAsync(
            Func<AggregateState<LoyaltyPoints.LoyaltyPoint>> getAggregateState,
            AddLoyaltyPoint command)
        {
            await Task.CompletedTask;
            if (getAggregateState().Payload.LastOccuredTime is not null && getAggregateState().Payload.LastOccuredTime > command.HappenedDate)
            {
                throw new SekibanLoyaltyPointCanNotHappenOnThisTimeException();
            }
            yield return new LoyaltyPointAdded(command.HappenedDate, command.Reason, command.PointAmount, command.Note);
        }
    }
}
