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
using FeatureCheck.Domain.Aggregates.RecentActivities.Projections;
using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities;
using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShippingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShippingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Commands;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;
using FeatureCheck.Domain.EventSubscribers;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Projections.DissolvableProjection;
using Sekiban.Core.Dependency;
using System.Reflection;
namespace FeatureCheck.Domain.Shared;

public class FeatureCheckDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly()
    {
        return Assembly.GetExecutingAssembly();
    }

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
            .AddCommandHandler<OnlyPublishingAddRecentActivity, OnlyPublishingAddRecentActivity.Handler>()
            .AddSingleProjection<TenRecentProjection>()
            .AddSingleProjectionListQuery<TenRecentQuery>();

        AddAggregate<RecentInMemoryActivity>()
            .AddCommandHandler<CreateRecentInMemoryActivity, CreateRecentInMemoryActivity.Handler>()
            .AddCommandHandler<AddRecentInMemoryActivity, AddRecentInMemoryActivity.Handler>();

        AddAggregate<VersionCheckAggregate>()
            .AddCommandHandler<OldV1Command, OldV1Command.Handler>()
            .AddCommandHandler<OldV2Command, OldV2Command.Handler>()
            .AddCommandHandler<CurrentV3Command, CurrentV3Command.Handler>();

        AddAggregate<ICartAggregate>()
            .AddSubtype<ShoppingCartI>(
                subType =>
                    subType.AddCommandHandler<AddItemToShoppingCartI, AddItemToShoppingCartI.Handler>()
                        .AddCommandHandler<SubmitOrderI, SubmitOrderI.Handler>())
            .AddSubtype<PurchasedCartI>(
                subType =>
                    subType.AddCommandHandler<ReceivePaymentToPurchasedCartI, ReceivePaymentToPurchasedCartI.Handler>())
            .AddSubtype<ShippingCartI>(subType => { });

        AddAggregate<CartAggregateR>()
            .AddSubtype<ShoppingCartR>(
                subType =>
                    subType.AddCommandHandler<AddItemToShoppingCartR, AddItemToShoppingCartR.Handler>()
                        .AddCommandHandler<SubmitOrderR, SubmitOrderR.Handler>())
            .AddSubtype<PurchasedCartR>(
                subType =>
                    subType.AddCommandHandler<ReceivePaymentToPurchasedCartR, ReceivePaymentToPurchasedCartR.Handler>())
            .AddSubtype<ShippingCartR>(subType => { });

        AddMultiProjectionQuery<ClientLoyaltyPointMultiProjectionQuery>();
        AddMultiProjectionListQuery<ClientLoyaltyPointQuery>();
        AddMultiProjectionQuery<DissolvableProjectionQuery>();
    }
}
