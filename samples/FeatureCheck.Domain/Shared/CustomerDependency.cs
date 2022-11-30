using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Branches.Queries;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Aggregates.Clients.Queries;
using Customer.Domain.Aggregates.Clients.Queries.BasicClientFilters;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Customer.Domain.Aggregates.RecentActivities.Commands;
using Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;
using Customer.Domain.EventSubscribers;
using Customer.Domain.Projections.ClientLoyaltyPointLists;
using Customer.Domain.Projections.ClientLoyaltyPointMultiples;
using Sekiban.Core.Dependency;
using System.Reflection;
namespace Customer.Domain.Shared;

public class CustomerDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    protected override void Define()
    {
        AddAggregate<Branch>()
            .AddCommandHandler<CreateBranch, CreateBranch.Handler>()
            .AddAggregateQuery<BranchExistsQuery>();

        AddAggregate<Aggregates.Clients.Client>()
            .AddCommandHandler<CreateClient, CreateClient.Handler>()
            .AddCommandHandler<ChangeClientName, ChangeClientName.Handler>()
            .AddCommandHandler<DeleteClient, DeleteClient.Handler>()
            .AddCommandHandler<CancelDeleteClient, CancelDeleteClient.Handler>()
            .AddEventSubscriber<ClientCreated, ClientCreatedSubscriber>()
            .AddEventSubscriber<ClientDeleted, ClientDeletedSubscriber>()
            .AddSingleProjection<ClientNameHistoryProjection>()
            .AddSingleProjectionListQuery<ClientNameHistoryProjectionQuery>()
            .AddAggregateQuery<ClientEmailExistsQuery>()
            .AddAggregateListQuery<BasicClientQuery>()
            .AddSingleProjectionQuery<ClientNameHistoryProjectionCountQuery>();

        AddAggregate<Aggregates.LoyaltyPoints.LoyaltyPoint>()
            .AddCommandHandler<CreateLoyaltyPoint, CreateLoyaltyPoint.Handler>()
            .AddCommandHandler<LoyaltyPointAndAddPoint, LoyaltyPointAndAddPoint.Handler>()
            .AddCommandHandler<AddLoyaltyPoint, AddLoyaltyPoint.Handler>()
            .AddCommandHandler<UseLoyaltyPoint, UseLoyaltyPoint.Handler>()
            .AddCommandHandler<DeleteLoyaltyPoint, DeleteLoyaltyPoint.Handler>();

        AddAggregate<Aggregates.RecentActivities.RecentActivity>()
            .AddCommandHandler<RecentActivity, RecentActivity.Handler>()
            .AddCommandHandler<AddRecentActivity, AddRecentActivity.Handler>()
            .AddCommandHandler<OnlyPublishingAddRecentActivity, OnlyPublishingAddRecentActivity.Handler>();
        
        AddAggregate<Aggregates.RecentInMemoryActivities.RecentInMemoryActivity>()
            .AddCommandHandler<CreateRecentInMemoryActivity, CreateRecentInMemoryActivity.Handler>()
            .AddCommandHandler<AddRecentInMemoryActivity, AddRecentInMemoryActivity.Handler>();

        AddMultiProjectionQuery<ClientLoyaltyPointMultiProjectionQuery>();
        AddMultiProjectionListQuery<ClientLoyaltyPointQuery>();

    }
}
