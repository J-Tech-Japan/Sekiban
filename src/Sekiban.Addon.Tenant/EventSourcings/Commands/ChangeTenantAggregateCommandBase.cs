using Sekiban.Addon.Tenant.EventSourcings.Aggregates;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
namespace Sekiban.Addon.Tenant.EventSourcings.Commands;

public abstract record ChangeTenantAggregateCommandBase<TAggregate> : ChangeAggregateCommandBase<TAggregate>, ITenantCommand
    where TAggregate : IAggregate, ITenantAggregate
{
    public Guid TenantId { get; init; }
}
