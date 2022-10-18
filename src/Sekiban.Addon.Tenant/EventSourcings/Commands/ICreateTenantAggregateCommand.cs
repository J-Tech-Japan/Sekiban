using Sekiban.Addon.Tenant.EventSourcings.Aggregates;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
namespace Sekiban.Addon.Tenant.EventSourcings.Commands;

public interface ICreateTenantAggregateCommand<TAggregate> : ICreateAggregateCommand<TAggregate>, ITenantCommand
    where TAggregate : ITenantAggregate, IAggregate
{
}
