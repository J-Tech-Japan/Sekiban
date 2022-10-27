using Customer.Domain.AggregateEventSubscribers;
using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Branches.QueryFilters;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Aggregates.Clients.QueryFilters;
using Customer.Domain.Aggregates.Clients.QueryFilters.BasicClientFilters;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Customer.Domain.Aggregates.RecentActivities;
using Customer.Domain.Aggregates.RecentActivities.Commands;
using Customer.Domain.Aggregates.RecentInMemoryActivities;
using Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;
using Customer.Domain.Projections.ClientLoyaltyPointLists;
using Customer.Domain.Projections.ClientLoyaltyPointMultiples;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Event;
using System.Reflection;
namespace Customer.Domain.Shared;

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
        yield return typeof(ClientEmailExistsQueryFilter);
        yield return typeof(BranchExistsQueryFilter);
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

        yield return (typeof(IChangeAggregateCommandHandler<Client, CancelDeleteClient>), typeof(CancelDeleteClientHandler));

        // Aggregate: LoyaltyPoint
        yield return (typeof(ICreateAggregateCommandHandler<LoyaltyPoint, CreateLoyaltyPoint>), typeof(CreateLoyaltyPointHandler));

        yield return (typeof(IChangeAggregateCommandHandler<LoyaltyPoint, AddLoyaltyPoint>), typeof(AddLoyaltyPointHandler));

        yield return (typeof(IChangeAggregateCommandHandler<LoyaltyPoint, UseLoyaltyPoint>), typeof(UseLoyaltyPointHandler));

        yield return (typeof(IChangeAggregateCommandHandler<LoyaltyPoint, DeleteLoyaltyPoint>), typeof(DeleteLoyaltyPointHandler));

        // Aggregate: RecentActivity
        yield return (typeof(ICreateAggregateCommandHandler<RecentActivity, CreateRecentActivity>), typeof(CreateRecentActivityHandler));

        yield return (typeof(IChangeAggregateCommandHandler<RecentActivity, AddRecentActivity>), typeof(AddRecentActivityHandler));
        yield return (typeof(IChangeAggregateCommandHandler<RecentActivity, OnlyPublishingAddRecentActivity>),
            typeof(OnlyPublishingAddRecentActivityHandler));
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
    public bool ShouldMakeSimpleAggregateListQueryFilter => true;
    public bool ShouldMakeSimpleSingleAggregateProjectionListQueryFilter => true;
}
