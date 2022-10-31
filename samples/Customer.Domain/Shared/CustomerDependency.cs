using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Branches.Queries;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Aggregates.Clients.Queries;
using Customer.Domain.Aggregates.Clients.Queries.BasicClientFilters;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Customer.Domain.Aggregates.RecentActivities;
using Customer.Domain.Aggregates.RecentActivities.Commands;
using Customer.Domain.Aggregates.RecentInMemoryActivities;
using Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;
using Customer.Domain.EventSubscribers;
using Customer.Domain.Projections.ClientLoyaltyPointLists;
using Customer.Domain.Projections.ClientLoyaltyPointMultiples;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using System.Reflection;
namespace Customer.Domain.Shared;

public class CustomerDependency : IDependencyDefinition
{
    public Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();

    public IEnumerable<Type> GetAggregateListQueryTypes()
    {
        yield return typeof(BasicClientQuery);
    }
    public IEnumerable<Type> GetAggregateQueryTypes()
    {
        yield return typeof(ClientEmailExistsQuery);
        yield return typeof(BranchExistsQuery);
    }
    public IEnumerable<Type> GetSingleProjectionListQueryTypes()
    {
        yield return typeof(ClientNameHistoryProjectionQuery);
    }
    public IEnumerable<Type> GetSingleProjectionQueryTypes() => Enumerable.Empty<Type>();
    public IEnumerable<Type> GetMultiProjectionQueryTypes()
    {
        yield return typeof(ClientLoyaltyPointMultipleMultiProjectionQuery);
    }
    public IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies()
    {
        // Aggregate Event Subscribers
        yield return (typeof(INotificationHandler<Event<ClientCreated>>), typeof(ClientCreatedSubscriber));

        yield return (typeof(INotificationHandler<Event<ClientDeleted>>), typeof(ClientDeletedSubscriber));

        // Aggregate: Branch
        yield return (typeof(ICreateCommandHandler<Branch, CreateBranch>), typeof(CreateBranchHandler));

        // Aggregate: Client
        yield return (typeof(ICreateCommandHandler<Client, CreateClient>), typeof(CreateClient.Handler));

        yield return (typeof(IChangeCommandHandler<Client, ChangeClientName>), typeof(ChangeClientName.Handler));

        yield return (typeof(IChangeCommandHandler<Client, DeleteClient>), typeof(DeleteClient.Handler));

        yield return (typeof(IChangeCommandHandler<Client, CancelDeleteClient>), typeof(CancelDeleteClient.Handler));

        // Aggregate: LoyaltyPoint
        yield return (typeof(ICreateCommandHandler<LoyaltyPoint, CreateLoyaltyPoint>), typeof(CreateLoyaltyPointHandler));

        yield return (typeof(IChangeCommandHandler<LoyaltyPoint, AddLoyaltyPoint>), typeof(AddLoyaltyPoint.Handler));

        yield return (typeof(IChangeCommandHandler<LoyaltyPoint, UseLoyaltyPoint>), typeof(UseLoyaltyPointHandler));

        yield return (typeof(IChangeCommandHandler<LoyaltyPoint, DeleteLoyaltyPoint>), typeof(DeleteLoyaltyPointHandler));

        // Aggregate: RecentActivity
        yield return (typeof(ICreateCommandHandler<RecentActivity, CreateRecentActivity>), typeof(CreateRecentActivityHandler));

        yield return (typeof(IChangeCommandHandler<RecentActivity, AddRecentActivity>), typeof(AddRecentActivityHandler));
        yield return (typeof(IChangeCommandHandler<RecentActivity, OnlyPublishingAddRecentActivity>),
            typeof(OnlyPublishingAddRecentActivityHandler));
        // Aggregate: RecentInMemoryActivity
        yield return (typeof(ICreateCommandHandler<RecentInMemoryActivity, CreateRecentInMemoryActivity>),
            typeof(CreateRecentInMemoryActivityHandler));

        yield return (typeof(IChangeCommandHandler<RecentInMemoryActivity, AddRecentInMemoryActivity>),
            typeof(AddRecentInMemoryActivityHandler));
    }
    public IEnumerable<(Type serviceType, Type? implementationType)> GetSubscriberDependencies() =>
        Enumerable.Empty<(Type serviceType, Type? implementationType)>();
    public IEnumerable<Type> GetMultiProjectionListQueryTypes()
    {
        yield return typeof(ClientLoyaltyPointQuery);
    }
}
