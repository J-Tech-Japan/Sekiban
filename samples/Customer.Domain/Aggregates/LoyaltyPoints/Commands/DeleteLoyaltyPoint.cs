﻿using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
namespace Customer.Domain.Aggregates.LoyaltyPoints.Commands;

public record DeleteLoyaltyPoint(Guid ClientId) : ChangeAggregateCommandBase<LoyaltyPoint>, INoValidateCommand
{
    public DeleteLoyaltyPoint() : this(Guid.Empty) { }
    public override Guid GetAggregateId()
    {
        return ClientId;
    }
}
public class DeleteLoyaltyPointHandler : ChangeAggregateCommandHandlerBase<LoyaltyPoint, DeleteLoyaltyPoint>
{
    protected override async IAsyncEnumerable<IChangedEvent<LoyaltyPoint>> ExecCommandAsync(
        AggregateState<LoyaltyPoint> aggregateState,
        DeleteLoyaltyPoint command)
    {
        await Task.CompletedTask;
        yield return new LoyaltyPointDeleted();
    }
}