using ResultBoxes;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using System.Collections.Immutable;
namespace Pure.Domain;

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
