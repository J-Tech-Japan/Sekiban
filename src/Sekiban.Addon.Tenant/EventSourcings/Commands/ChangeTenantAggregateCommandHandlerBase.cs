using Sekiban.Addon.Tenant.EventSourcings.Aggregates;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
namespace Sekiban.Addon.Tenant.EventSourcings.Commands;

public abstract class ChangeTenantAggregateCommandHandlerBase<TAggregate, TCommand> : ChangeAggregateCommandHandlerBase<TAggregate, TCommand>
    where TAggregate : IAggregate, ITenantAggregate where TCommand : ChangeTenantAggregateCommandBase<TAggregate>, new()
{
}
