using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.Commands;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.Commands;
using Sekiban.EventSourcing.AggregateCommands;
using System.Reflection;
namespace Sekiban.EventSourcing.Addon.Tenant.Shared;

public class TenantAddonDependency
{

    public static Assembly GetAssembly()
    {
        return Assembly.GetExecutingAssembly();
    }

    public static IEnumerable<Type> GetControllerAggregateTypes()
    {
        yield return typeof(SekibanTenant);
        yield return typeof(SekibanMember);
    }
    public static IEnumerable<Type> GetSingleAggregateProjectionTypes()
    {
        yield break;
    }
    public static IEnumerable<Type> GetMultipleAggregatesProjectionTypes()
    {
        yield break;
    }
    public static IEnumerable<Type> GetMultipleAggregatesListProjectionTypes()
    {
        yield break;
    }
    public static IEnumerable<(Type serviceType, Type? implementationType)> GetTransientDependencies()
    {
        // Aggregate: SekibanTenant
        yield return (typeof(ICreateAggregateCommandHandler<SekibanTenant, CreateSekibanTenant>), typeof(CreateSekibanTenantHandler));
        // Aggregate: SekibanMember
        yield return (typeof(ICreateAggregateCommandHandler<SekibanMember, CreateSekibanMember>), typeof(CreateSekibanMemberHandler));
    }
}
