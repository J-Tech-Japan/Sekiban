using Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Aggregates;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Commands;

public abstract class ChangeTenantAggregateCommandHandlerBase<TAggregate, TCommand> : ChangeAggregateCommandHandlerBase<TAggregate, TCommand>
    where TAggregate : IAggregate, ITenantAggregate where TCommand : ChangeTenantAggregateCommandBase<TAggregate>, new() { }
