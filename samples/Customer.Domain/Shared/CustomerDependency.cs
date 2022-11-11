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
using Sekiban.Core.Dependency;
using System.Reflection;
namespace Customer.Domain.Shared;

public class CustomerDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    protected override void Define()
    {
        AddAggregate<Branch>()
            .AddCreateCommandHandler<CreateBranch, CreateBranch.Handler>()
            .AddAggregateQuery<BranchExistsQuery>();

        AddAggregate<Client>()
            .AddCreateCommandHandler<CreateClient, CreateClient.Handler>()
            .AddChangeCommandHandler<ChangeClientName, ChangeClientName.Handler>()
            .AddChangeCommandHandler<DeleteClient, DeleteClient.Handler>()
            .AddChangeCommandHandler<CancelDeleteClient, CancelDeleteClient.Handler>()
            .AddEventSubscriber<ClientCreated, ClientCreatedSubscriber>()
            .AddEventSubscriber<ClientDeleted, ClientDeletedSubscriber>()
            .AddSingleProjection<ClientNameHistoryProjection>()
            .AddSingleProjectionListQuery<ClientNameHistoryProjectionQuery>()
            .AddAggregateQuery<ClientEmailExistsQuery>()
            .AddAggregateListQuery<BasicClientQuery>();

        AddAggregate<LoyaltyPoint>()
            .AddCreateCommandHandler<CreateLoyaltyPoint, CreateLoyaltyPoint.Handler>()
            .AddChangeCommandHandler<AddLoyaltyPoint, AddLoyaltyPoint.Handler>()
            .AddChangeCommandHandler<UseLoyaltyPoint, UseLoyaltyPoint.Handler>()
            .AddChangeCommandHandler<DeleteLoyaltyPoint, DeleteLoyaltyPoint.Handler>();

        AddAggregate<RecentActivity>()
            .AddCreateCommandHandler<CreateRecentActivity, CreateRecentActivity.Handler>()
            .AddChangeCommandHandler<AddRecentActivity, AddRecentActivity.Handler>()
            .AddChangeCommandHandler<OnlyPublishingAddRecentActivity, OnlyPublishingAddRecentActivity.Handler>();

        AddAggregate<RecentInMemoryActivity>()
            .AddCreateCommandHandler<CreateRecentInMemoryActivity, CreateRecentInMemoryActivity.Handler>()
            .AddChangeCommandHandler<AddRecentInMemoryActivity, AddRecentInMemoryActivity.Handler>();

        AddMultiProjectionQuery<ClientLoyaltyPointMultipleMultiProjectionQuery>();
        AddMultiProjectionListQuery<ClientLoyaltyPointQuery>();

    }
}
