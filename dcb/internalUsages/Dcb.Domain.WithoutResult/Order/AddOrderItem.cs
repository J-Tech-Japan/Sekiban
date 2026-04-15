using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;

namespace Dcb.Domain.WithoutResult.Order;

public sealed record AddOrderItem : ICommandWithHandler<AddOrderItem>
{
    public Guid OrderId { get; init; }
    public Guid ItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public DateTimeOffset? AddedAt { get; init; }

    public static async Task<EventOrNone> HandleAsync(AddOrderItem command, ICommandContext context)
    {
        var tag = new OrderTag(command.OrderId);
        if (!await context.TagExistsAsync(tag).ConfigureAwait(false))
        {
            throw new ApplicationException($"Order {command.OrderId} does not exist.");
        }

        var state = await context.GetStateAsync<OrderProjector>(tag).ConfigureAwait(false);
        if (state.Payload is OrderState payload && payload.Status == "Cancelled")
        {
            throw new ApplicationException($"Order {command.OrderId} is cancelled.");
        }

        var itemId = command.ItemId == Guid.Empty ? Guid.CreateVersion7() : command.ItemId;
        return EventOrNone.From(
            new OrderItemAdded(
                command.OrderId,
                itemId,
                command.ProductName,
                command.Quantity,
                command.UnitPrice,
                command.AddedAt ?? DateTimeOffset.UtcNow),
            tag);
    }
}
