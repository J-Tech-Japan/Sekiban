using FeatureCheck.Domain.Aggregates.ALotOfEvents;
using FeatureCheck.Domain.Aggregates.ALotOfEvents.Commands;
using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Branches.Queries;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Aggregates.Clients.Queries;
using FeatureCheck.Domain.Aggregates.Clients.Queries.BasicClientFilters;
using FeatureCheck.Domain.Aggregates.DerivedTypes;
using FeatureCheck.Domain.Aggregates.DerivedTypes.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.MultipleEventsInAFiles;
using FeatureCheck.Domain.Aggregates.RecentActivities;
using FeatureCheck.Domain.Aggregates.RecentActivities.Commands;
using FeatureCheck.Domain.Aggregates.RecentActivities.Projections;
using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities;
using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes;
using FeatureCheck.Domain.Aggregates.SubTypes.BaseIsFirstSubtypes.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.PurchasedCarts.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShippingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes.Subtypes.ShoppingCarts.Events;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.PurchasedCarts.Commands;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShippingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts;
using FeatureCheck.Domain.Aggregates.SubTypes.RecordBaseTypes.Subtypes.ShoppingCarts.Commands;
using FeatureCheck.Domain.Aggregates.TenantUsers;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates;
using FeatureCheck.Domain.Aggregates.VersionCheckAggregates.Commands;
using FeatureCheck.Domain.Common;
using FeatureCheck.Domain.EventSubscribers;
using FeatureCheck.Domain.Projections;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Projections.DissolvableProjection;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using System.Reflection;
namespace FeatureCheck.Domain.Shared;

public class FeatureCheckDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();

    public override void Define()
    {
        AddAggregate<Branch>()
            .AddCommandHandler<CreateBranch, CreateBranch.Handler>()
            .AddCommandHandler<CreateBranchWithRootPartitionKey, CreateBranchWithRootPartitionKey.Handler>()
            .AddCommandHandler<AddNumberOfClients, AddNumberOfClients.Handler>()
            .AddCommandHandler<NotAddingAnyEventCommand, NotAddingAnyEventCommand.Handler>()
            .AddAggregateQuery<BranchExistsQuery>();

        AddServices(services => { services.AddTransient<DependencyInjectionSampleService>(); });

        AddAggregate<Client>()
            .AddCommandHandler<CreateClient, CreateClient.Handler>()
            .AddCommandHandler<CreateClientWithBranchSubscriber, CreateClientWithBranchSubscriber.Handler>()
            .AddCommandHandler<ChangeClientName, ChangeClientName.Handler>()
            .AddCommandHandler<DeleteClient, DeleteClient.Handler>()
            .AddCommandHandler<CancelDeleteClient, CancelDeleteClient.Handler>()
            .AddCommandHandler<ChangeClientNameWithoutLoading, ChangeClientNameWithoutLoading.Handler>()
            .AddEventSubscriberWithNonBlocking<ClientCreatedWithBranchAdd,
                ClientCreatedWithBranchAdd.BranchSubscriber>()
            .AddEventSubscriber<ClientCreated, ClientCreatedSubscriber>()
            .AddEventSubscriber<ClientDeleted, ClientDeletedSubscriber>()
            .AddSingleProjection<ClientNameHistoryProjection>()
            .AddSingleProjectionListQuery<ClientNameHistoryProjectionQuery>()
            .AddAggregateQuery<ClientEmailExistsQuery>()
            .AddAggregateListQuery<BasicClientQuery>()
            .AddSingleProjectionQuery<ClientNameHistoryProjectionCountQuery>()
            .AddCommandHandler<ClientNoEventsCommand, ClientNoEventsCommand.Handler>()
            .AddAggregateListQuery<GetClientPayloadQuery>();

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
            .AddCommandHandler<OnlyPublishingAddRecentActivityAsync, OnlyPublishingAddRecentActivityAsync.Handler>()
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
                subType => subType
                    .AddCommandHandler<AddItemToShoppingCartI, AddItemToShoppingCartI.Handler>()
                    .AddCommandHandler<SubmitOrderI, SubmitOrderI.Handler>()
                    .AddEventSubscriber<OrderSubmittedI, OrderSubmittedI.Subscriber>())
            .AddSubtype<PurchasedCartI>(
                subType => subType
                    .AddCommandHandler<ReceivePaymentToPurchasedCartI, ReceivePaymentToPurchasedCartI.Handler>())
            .AddSubtype<ShippingCartI>(_ => { });

        AddAggregate<CartAggregateR>()
            .AddSubtype<ShoppingCartR>(
                subType => subType
                    .AddCommandHandler<AddItemToShoppingCartR, AddItemToShoppingCartR.Handler>()
                    .AddCommandHandler<SubmitOrderR, SubmitOrderR.Handler>())
            .AddSubtype<PurchasedCartR>(
                subType => subType
                    .AddCommandHandler<ReceivePaymentToPurchasedCartR, ReceivePaymentToPurchasedCartR.Handler>())
            .AddSubtype<ShippingCartR>(_ => { });

        AddMultiProjectionQuery<ClientLoyaltyPointMultiProjectionQuery>();
        AddMultiProjectionListQuery<ClientLoyaltyPointQuery>();
        AddMultiProjectionQuery<DissolvableProjectionQuery>();
        AddMultiProjectionQuery<ClientLoyaltyPointExceptionTestQuery>();
        AddMultiProjectionListQuery<ClientLoyaltyPointExceptionTestListQuery>();
        AddGeneralQuery<GeneralQuerySample>();
        AddGeneralListQuery<GeneralListQuerySample>();

        AddAggregate<IInheritedAggregate>()
            .AddSubtype<ProcessingSubAggregate>(
                subType => subType
                    .AddCommandHandler<OpenInheritedAggregate, OpenInheritedAggregate.Handler>()
                    .AddCommandHandler<CloseInheritedAggregate, CloseInheritedAggregate.Handler>())
            .AddSubtype<ClosedSubAggregate>(
                subType => subType.AddCommandHandler<ReopenInheritedAggregate, ReopenInheritedAggregate.Handler>());

        AddAggregate<ALotOfEventsAggregate>()
            .AddCommandHandler<ALotOfEventsCreateCommand, ALotOfEventsCreateCommand.Handler>();

        AddAggregate<BaseFirstAggregate>()
            .AddCommandHandler<BFAggregateCreateAccount, BFAggregateCreateAccount.Handler>()
            .AddCommandHandler<ActivateBFAggregate, ActivateBFAggregate.Handler>()
            .AddSubtype<ActiveBFAggregate>(
                subType => subType.AddCommandHandler<CloseBFAggregate, CloseBFAggregate.Handler>())
            .AddSubtype<ClosedBFAggregate>(
                subType => subType.AddCommandHandler<ReopenBFAggregate, ReopenBFAggregate.Handler>());

        AddAggregate<Booking>()
            .AddCommandHandler<BookingCommands.BookRoom, BookingCommands.BookRoom.Handler>()
            .AddCommandHandler<BookingCommands.PayBookedRoom, BookingCommands.PayBookedRoom.Handler>();

        AddAggregate<DerivedTypeAggregate>()
            .AddCommandHandler<CreateVehicle, CreateVehicle.Handler>()
            .AddCommandHandler<CreateCar, CreateCar.Handler>();

        AddAggregate<TenantUser>().AddAggregateQuery<TenantUserDuplicateEmailQuery>();

        AddAggregate<IInheritInSubtypesType>()
            .AddSubtype<FirstStage>(sub => sub.AddCommandHandler<ChangeToSecondYield, ChangeToSecondYield.Handler>())
            .AddSubtype<SecondStage>(
                sub => sub.AddCommandHandler<MoveBackToFirstYield, MoveBackToFirstYield.Handler>());
    }
}
