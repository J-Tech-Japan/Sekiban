using CustomerDomainContext.AggregateEventSubscribers;
using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.Clients.Projections;
using CustomerDomainContext.Aggregates.Clients.QueryFilters.BasicClientFilters;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
using CustomerDomainContext.Aggregates.RecentActivities;
using CustomerDomainContext.Aggregates.RecentActivities.Commands;
using CustomerDomainContext.Aggregates.RecentInMemoryActivities;
using CustomerDomainContext.Aggregates.RecentInMemoryActivities.Commands;
using CustomerDomainContext.Projections.ClientLoyaltyPointLists;
using CustomerDomainContext.Projections.ClientLoyaltyPointMultiples;
using Sekiban.EventSourcing.Shared;
using System.Reflection;
namespace CustomerDomainContext.Shared;

public class CustomerDependency : IDependencyDefinition
{
    public Assembly GetExecutingAssembly()
    {
        return Assembly.GetExecutingAssembly();
    }
    public IEnumerable<Type> GetControllerAggregateTypes()
    {
        yield return typeof(Branch);
        yield return typeof(Client);
        yield return typeof(LoyaltyPoint);
        yield return typeof(RecentActivity);
        yield return typeof(RecentInMemoryActivity);
    }

    public IEnumerable<Type> GetSingleAggregateProjectionTypes()
    {
        yield return typeof(ClientNameHistoryProjection);
    }
    public IEnumerable<Type> GetMultipleAggregatesProjectionTypes()
    {
        yield return typeof(ClientLoyaltyPointMultipleProjection);
        yield return typeof(ClientLoyaltyPointListProjection);
    }


    public IEnumerable<Type> GetAggregateListQueryFilterTypes()
    {
        yield return typeof(BasicClientQueryFilter);
    }
    public IEnumerable<Type> GetAggregateQueryFilterTypes()
    {
        return Enumerable.Empty<Type>();
    }
    public IEnumerable<Type> GetSingleAggregateProjectionListQueryFilterTypes()
    {
        yield return typeof(ClientNameHistoryProjectionQueryFilter);
    }
    public IEnumerable<Type> GetSingleAggregateProjectionQueryFilterTypes()
    {
        return Enumerable.Empty<Type>();
    }
    public IEnumerable<Type> GetProjectionQueryFilterTypes()
    {
        yield return typeof(ClientLoyaltyPointMultipleProjectionQueryFilter);
    }
    public IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies()
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
    }
    public IEnumerable<Type> GetProjectionListQueryFilterTypes()
    {
        yield return typeof(ClientLoyaltyPointQueryFilter);
    }
}
