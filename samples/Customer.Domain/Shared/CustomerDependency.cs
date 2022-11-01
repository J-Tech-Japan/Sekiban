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
            .CreateCommandHandler<CreateBranch, CreateBranchHandler>()
            .Query<BranchExistsQuery>();

        Aggregate<Client>()
            .CreateCommandHandler<CreateClient, CreateClient.Handler>()
            .ChangeCommandHandler<ChangeClientName, ChangeClientName.Handler>()
            .ChangeCommandHandler<DeleteClient, DeleteClient.Handler>()
            .ChangeCommandHandler<CancelDeleteClient, CancelDeleteClient.Handler>()
            .EventSubscriber<ClientCreated, ClientCreatedSubscriber>()
            .EventSubscriber<ClientDeleted, ClientDeletedSubscriber>()
            .SingleProjection<ClientNameHistoryProjection>()
            .Query<ClientNameHistoryProjectionQuery>()
            .Query<ClientEmailExistsQuery>();

        Aggregate<LoyaltyPoint>()
            .CreateCommandHandler<CreateLoyaltyPoint, CreateLoyaltyPointHandler>()
            .ChangeCommandHandler<AddLoyaltyPoint, AddLoyaltyPoint.Handler>()
            .ChangeCommandHandler<UseLoyaltyPoint, UseLoyaltyPointHandler>()
            .ChangeCommandHandler<DeleteLoyaltyPoint, DeleteLoyaltyPointHandler>();

        Aggregate<RecentActivity>()
            .CreateCommandHandler<CreateRecentActivity, CreateRecentActivityHandler>()
            .ChangeCommandHandler<AddRecentActivity, AddRecentActivityHandler>()
            .ChangeCommandHandler<OnlyPublishingAddRecentActivity, OnlyPublishingAddRecentActivityHandler>();

        Aggregate<RecentInMemoryActivity>()
            .CreateCommandHandler<CreateRecentInMemoryActivity, CreateRecentInMemoryActivityHandler>()
            .ChangeCommandHandler<AddRecentInMemoryActivity, AddRecentInMemoryActivityHandler>();

        MultiProjectionQuery<ClientLoyaltyPointMultipleMultiProjectionQuery>();
        MultiProjectionQuery<ClientLoyaltyPointQuery>();

    }
}
