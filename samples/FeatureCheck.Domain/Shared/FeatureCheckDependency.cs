using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Queries.BasicClientFilters;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.RecentActivities;
using FeatureCheck.Domain.Aggregates.RecentActivities.Commands;
using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities;
using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Commands;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;
using FeatureCheck.Domain.EventSubscribers;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using Sekiban.Core.Dependency;
using System.Reflection;
namespace FeatureCheck.Domain.Shared;

public class FeatureCheckDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();

    protected override void Define()
    {
        AddAggregate<Branch>()
            .AddCommandHandler<CreateBranch, CreateBranch.Handler>()
            .AddAggregateQuery<BranchExistsQuery>();

        AddAggregate<Client>()
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

        AddAggregate<LoyaltyPoint>()
            .AddCommandHandler<CreateLoyaltyPoint, CreateLoyaltyPoint.Handler>()
            .AddCommandHandler<CreateLoyaltyPointAndAddPoint, CreateLoyaltyPointAndAddPoint.Handler>()
            .AddCommandHandler<AddLoyaltyPoint, AddLoyaltyPoint.Handler>()
            .AddCommandHandler<UseLoyaltyPoint, UseLoyaltyPoint.Handler>()
            .AddCommandHandler<DeleteLoyaltyPoint, DeleteLoyaltyPoint.Handler>()
            .AddCommandHandler<AddLoyaltyPointWithVO, AddLoyaltyPointWithVO.Handler>();

        AddAggregate<RecentActivity>()
            .AddCommandHandler<CreateRecentActivity, CreateRecentActivity.Handler>()
            .AddCommandHandler<AddRecentActivity, AddRecentActivity.Handler>()
            .AddCommandHandler<OnlyPublishingAddRecentActivity, OnlyPublishingAddRecentActivity.Handler>();

        AddAggregate<RecentInMemoryActivity>()
            .AddCommandHandler<CreateRecentInMemoryActivity, CreateRecentInMemoryActivity.Handler>()
            .AddCommandHandler<AddRecentInMemoryActivity, AddRecentInMemoryActivity.Handler>();

        AddAggregate<VersionCheckAggregate>()
            .AddCommandHandler<OldV1Command, OldV1Command.Handler>()
            .AddCommandHandler<OldV2Command, OldV2Command.Handler>()
            .AddCommandHandler<CurrentV3Command, CurrentV3Command.Handler>();

        AddMultiProjectionQuery<ClientLoyaltyPointMultiProjectionQuery>();
        AddMultiProjectionListQuery<ClientLoyaltyPointQuery>();
    }
}