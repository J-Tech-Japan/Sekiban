using MultiTenant.Domain.Aggregates.Clients;
using MultiTenant.Domain.Aggregates.Clients.Commands;
using MultiTenant.Domain.Aggregates.Clients.Queries;
using Sekiban.Core.Dependency;
using System.Reflection;
namespace MultiTenant.Domain.Aggregates;

public class MultiTenantDependency : DomainDependencyDefinitionBase
{

    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    public override void Define()
    {
    }
}
