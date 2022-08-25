using CustomerWithTenantAddonDomainContext.AggregateEventSubscribers;
using CustomerWithTenantAddonDomainContext.Aggregates.Branches;
using CustomerWithTenantAddonDomainContext.Aggregates.Branches.Commands;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients.Commands;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients.Events;
using CustomerWithTenantAddonDomainContext.Aggregates.Clients.Projections;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints;
using CustomerWithTenantAddonDomainContext.Aggregates.LoyaltyPoints.Commands;
using CustomerWithTenantAddonDomainContext.Aggregates.RecentActivities;
using CustomerWithTenantAddonDomainContext.Aggregates.RecentActivities.Commands;
using CustomerWithTenantAddonDomainContext.Aggregates.RecentInMemoryActivities;
using CustomerWithTenantAddonDomainContext.Aggregates.RecentInMemoryActivities.Commands;
using CustomerWithTenantAddonDomainContext.Projections;
using Sekiban.EventSourcing.Addon.Tenant.Shared;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.TestHelpers;
using System.Reflection;
namespace CustomerWithTenantAddonDomainContext.Shared;

public static class CustomerWithTenantAddonDependency
{
    public static Assembly GetAssembly() =>
        Assembly.GetExecutingAssembly();

    public static RegisteredEventTypes GetEventTypes() =>
        new(GetAssembly(), TenantAddonDependency.GetAssembly(), SekibanEventSourcingDependency.GetAssembly());

    public static SekibanAggregateTypes GetAggregateTypes() =>
        new(GetAssembly(), SekibanEventSourcingDependency.GetAssembly(), TenantAddonDependency.GetAssembly());
    public static IEnumerable<Type> GetControllerAggregateTypes()
    {
        yield return typeof(Branch);
        yield return typeof(Client);
        yield return typeof(LoyaltyPoint);
        yield return typeof(RecentActivity);
        yield return typeof(RecentInMemoryActivity);
        foreach (var aggregate in TenantAddonDependency.GetControllerAggregateTypes())
        {
            yield return aggregate;
        }
    }
    public static IEnumerable<Type> GetSingleAggregateProjectionTypes()
    {
        yield return typeof(ClientNameHistoryProjection);
    }
    public static IEnumerable<Type> GetMultipleAggregatesProjectionTypes()
    {
        yield return typeof(ClientLoyaltyPointMultipleProjection);
    }
    public static IEnumerable<Type> GetMultipleAggregatesListProjectionTypes()
    {
        yield return typeof(ClientLoyaltyPointListProjection);
    }
    public static IEnumerable<(Type serviceType, Type? implementationType)> GetTransientDependencies()
    {
        // Aggregate Event Subscribers
        yield return (typeof(INotificationHandler<AggregateEvent<ClientCreated>>), typeof(ClientCreatedSubscriber));

        yield return (typeof(INotificationHandler<AggregateEvent<ClientDeleted>>), typeof(ClientDeletedSubscriber));

        // Aggregate: Branch
        yield return (typeof(ICreateAggregateCommandHandler<Branch, CreateBranch>), typeof(CreateBranchHandler));

        // Aggregate: Client
        yield return (typeof(ICreateAggregateCommandHandler<Client, CreateClient>), typeof(CreateClientHandler));

        yield return (typeof(IChangeAggregateCommandHandler<Client, ChangeClientName>), typeof(ChangeClientNameHandler));

        yield return (typeof(IChangeAggregateCommandHandler<Client, DeleteClient>), typeof(DeleteClientHandler));

        // Aggregate: LoyaltyPoint
        yield return (typeof(ICreateAggregateCommandHandler<LoyaltyPoint, CreateLoyaltyPoint>), typeof(CreateLoyaltyPointHandler));

        yield return (typeof(IChangeAggregateCommandHandler<LoyaltyPoint, AddLoyaltyPoint>), typeof(AddLoyaltyPointHandler));

        yield return (typeof(IChangeAggregateCommandHandler<LoyaltyPoint, UseLoyaltyPoint>), typeof(UseLoyaltyPointHandler));

        yield return (typeof(IChangeAggregateCommandHandler<LoyaltyPoint, DeleteLoyaltyPoint>), typeof(DeleteLoyaltyPointHandler));

        // Aggregate: RecentActivity
        yield return (typeof(ICreateAggregateCommandHandler<RecentActivity, CreateRecentActivity>), typeof(CreateRecentActivityHandler));

        yield return (typeof(IChangeAggregateCommandHandler<RecentActivity, AddRecentActivity>), typeof(AddRecentActivityHandler));
        // Aggregate: RecentInMemoryActivity
        yield return (typeof(ICreateAggregateCommandHandler<RecentInMemoryActivity, CreateRecentInMemoryActivity>),
            typeof(CreateRecentInMemoryActivityHandler));

        yield return (typeof(IChangeAggregateCommandHandler<RecentInMemoryActivity, AddRecentInMemoryActivity>),
            typeof(AddRecentInMemoryActivityHandler));

        // SekibanTenantAddition
        foreach (var dependency in TenantAddonDependency.GetTransientDependencies())
        {
            yield return dependency;
        }
    }
    public static SekibanDependencyOptions GetOptions() =>
        new(GetEventTypes(), GetAggregateTypes(), GetTransientDependencies());
}
