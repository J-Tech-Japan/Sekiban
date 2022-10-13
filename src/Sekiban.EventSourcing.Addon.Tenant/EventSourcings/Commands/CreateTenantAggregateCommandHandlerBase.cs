using Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Aggregates;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Commands;

public abstract class CreateTenantAggregateCommandHandlerBase<TAggregate, TCommand> : CreateAggregateCommandHandlerBase<TAggregate, TCommand>
    where TAggregate : IAggregate, ITenantAggregate where TCommand : ICreateTenantAggregateCommand<TAggregate>, new()
{
}
