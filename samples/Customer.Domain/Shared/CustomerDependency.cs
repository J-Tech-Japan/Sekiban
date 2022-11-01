using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Branches.Queries;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Aggregates.Clients.Queries;
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
        Aggregate<Branch>()
            .AddCreateCommandHandler<CreateBranch, CreateBranchHandler>()
            .AddQuery<BranchExistsQuery>();

        Aggregate<Client>()
            .AddCreateCommandHandler<CreateClient, CreateClient.Handler>()
            .AddChangeCommandHandler<ChangeClientName, ChangeClientName.Handler>()
            .AddChangeCommandHandler<DeleteClient, DeleteClient.Handler>()
            .AddChangeCommandHandler<CancelDeleteClient, CancelDeleteClient.Handler>()
            .AddEventSubscriber<ClientCreated, ClientCreatedSubscriber>()
            .AddEventSubscriber<ClientDeleted, ClientDeletedSubscriber>()
            .AddSingleProjection<ClientNameHistoryProjection>()
            .AddQuery<ClientNameHistoryProjectionQuery>()
            .AddQuery<ClientEmailExistsQuery>();

        Aggregate<LoyaltyPoint>()
            .AddCreateCommandHandler<CreateLoyaltyPoint, CreateLoyaltyPointHandler>()
            .AddChangeCommandHandler<AddLoyaltyPoint, AddLoyaltyPoint.Handler>()
            .AddChangeCommandHandler<UseLoyaltyPoint, UseLoyaltyPointHandler>()
            .AddChangeCommandHandler<DeleteLoyaltyPoint, DeleteLoyaltyPointHandler>();

        Aggregate<RecentActivity>()
            .AddCreateCommandHandler<CreateRecentActivity, CreateRecentActivityHandler>()
            .AddChangeCommandHandler<AddRecentActivity, AddRecentActivityHandler>()
            .AddChangeCommandHandler<OnlyPublishingAddRecentActivity, OnlyPublishingAddRecentActivityHandler>();

        Aggregate<RecentInMemoryActivity>()
            .AddCreateCommandHandler<CreateRecentInMemoryActivity, CreateRecentInMemoryActivityHandler>()
            .AddChangeCommandHandler<AddRecentInMemoryActivity, AddRecentInMemoryActivityHandler>();

        AddMultiProjectionQuery<ClientLoyaltyPointMultipleMultiProjectionQuery>();
        AddMultiProjectionQuery<ClientLoyaltyPointQuery>();

    }
}
