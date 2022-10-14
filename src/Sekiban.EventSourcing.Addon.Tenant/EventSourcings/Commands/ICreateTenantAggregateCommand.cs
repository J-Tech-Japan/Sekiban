using Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Aggregates;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Commands;

public interface ICreateTenantAggregateCommand<TAggregate> : ICreateAggregateCommand<TAggregate>, ITenantCommand
    where TAggregate : ITenantAggregate, IAggregate
{
}
