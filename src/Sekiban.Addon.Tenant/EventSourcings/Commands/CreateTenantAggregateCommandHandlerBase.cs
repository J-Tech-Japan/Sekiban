using Sekiban.Addon.Tenant.EventSourcings.Aggregates;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
namespace Sekiban.Addon.Tenant.EventSourcings.Commands;

public abstract class CreateTenantAggregateCommandHandlerBase<TAggregate, TCommand> : CreateAggregateCommandHandlerBase<TAggregate, TCommand>
    where TAggregate : IAggregate, ITenantAggregate where TCommand : ICreateTenantAggregateCommand<TAggregate>, new()
{
}
