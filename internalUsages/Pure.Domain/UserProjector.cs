using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using System.Collections.Immutable;
namespace Pure.Domain;

public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) => (payload, ev.GetPayload()) switch
    {
        (EmptyAggregatePayload, UserRegistered registered) => new UnconfirmedUser(registered.Name, registered.Email),
        (UnconfirmedUser unconfirmedUser, UserConfirmed) => new ConfirmedUser(
            unconfirmedUser.Name,
            unconfirmedUser.Email),
        (ConfirmedUser confirmedUser, UserUnconfirmed) => new UnconfirmedUser(confirmedUser.Name, confirmedUser.Email),
        _ => payload
    };
    public string GetVersion() => "1.0.1";
}
public record MultiProjectorPayload(
    ImmutableDictionary<Guid, MultiProjectorPayload.User> Users,
    ImmutableDictionary<Guid, MultiProjectorPayload.Cart> Carts) : IMultiProjector<MultiProjectorPayload>
{
    public ResultBox<MultiProjectorPayload> Project(MultiProjectorPayload payload, IEvent ev) => ev.GetPayload() switch
    {
        UserRegistered userRegistered => payload with
        {
            Users = payload.Users.Add(
                ev.PartitionKeys.AggregateId,
                new User(ev.PartitionKeys.AggregateId, userRegistered.Name, userRegistered.Email, false))
        },
        UserConfirmed userConfirmed => payload.Users.TryGetValue(ev.PartitionKeys.AggregateId, out var existingUser)
            ? payload with
            {
                Users = payload.Users.SetItem(ev.PartitionKeys.AggregateId, existingUser with { IsConfirmed = true })
            }
            : payload,
        UserUnconfirmed => payload.Users.TryGetValue(ev.PartitionKeys.AggregateId, out var userToUnconfirm)
            ? payload with
            {
                Users = payload.Users.SetItem(
                    ev.PartitionKeys.AggregateId,
                    userToUnconfirm with { IsConfirmed = false })
            }
            : payload,
        ShoppingCartCreated => payload with
        {
            Carts = payload.Carts.Add(
                ev.PartitionKeys.AggregateId,
                new Cart(
                    ev.PartitionKeys.AggregateId,
                    ev.PartitionKeys.AggregateId,
                    ImmutableList<Item>.Empty,
                    string.Empty))
        },
        ShoppingCartItemAdded cartItemAdded => payload with
        {
            Carts = payload.Carts.TryGetValue(ev.PartitionKeys.AggregateId, out var cart)
                ? payload.Carts.SetItem(
                    ev.PartitionKeys.AggregateId,
                    cart with
                    {
                        Items = cart.Items.Add(
                            new Item(
                                cartItemAdded.ItemId,
                                cartItemAdded.Name,
                                cartItemAdded.Quantity,
                                cartItemAdded.Price))
                    })
                : payload.Carts
        },
        PaymentProcessedShoppingCart paymentProcessedShoppingCart => payload with
        {
            Carts = payload.Carts.TryGetValue(ev.PartitionKeys.AggregateId, out var cart)
                ? payload.Carts.SetItem(
                    ev.PartitionKeys.AggregateId,
                    cart with { PaymentMethod = paymentProcessedShoppingCart.PaymentMethod })
                : payload.Carts
        },
        _ => payload
    };
    public static MultiProjectorPayload GenerateInitialPayload()
        // ここで初期値を ImmutableDictionary にする
        => new(ImmutableDictionary<Guid, User>.Empty, ImmutableDictionary<Guid, Cart>.Empty);
    public record User(Guid UserId, string Name, string Email, bool IsConfirmed);
    public record Cart(Guid CartId, Guid UserId, ImmutableList<Item> Items, string PaymentMethod);
    public record Item(Guid ItemId, string Name, int Quantity, decimal Price);
}
public record
    UserQueryFromMultiProjection : IMultiProjectionListQuery<MultiProjectorPayload, UserQueryFromMultiProjection,
    string>
{
    public static ResultBox<IEnumerable<string>> HandleFilter(
        MultiProjectionState<MultiProjectorPayload> projection,
        UserQueryFromMultiProjection query,
        IQueryContext context) =>
        projection.Payload.Users.Values.Select(user => user.Name).ToList();
    public static ResultBox<IEnumerable<string>> HandleSort(
        IEnumerable<string> filteredList,
        UserQueryFromMultiProjection query,
        IQueryContext context) =>
        filteredList.OrderBy(name => name).ToList();
}
