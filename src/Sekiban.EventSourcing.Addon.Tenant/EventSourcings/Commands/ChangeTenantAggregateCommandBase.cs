using Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Aggregates;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Commands;

public abstract record ChangeTenantAggregateCommandBase<TAggregate> : ChangeAggregateCommandBase<TAggregate>, ITenantCommand
    where TAggregate : IAggregate, ITenantAggregate
{
    public Guid TenantId { get; init; }
}
