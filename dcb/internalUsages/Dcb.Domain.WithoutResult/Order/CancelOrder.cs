using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;

namespace Dcb.Domain.WithoutResult.Order;

public sealed record CancelOrder : ICommandWithHandler<CancelOrder>
{
    public Guid OrderId { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }

    public static async Task<EventOrNone> HandleAsync(CancelOrder command, ICommandContext context)
    {
        var tag = new OrderTag(command.OrderId);
        if (!await context.TagExistsAsync(tag).ConfigureAwait(false))
        {
            throw new ApplicationException($"Order {command.OrderId} does not exist.");
        }

        var state = await context.GetStateAsync<OrderProjector>(tag).ConfigureAwait(false);
        if (state.Payload is OrderState payload && payload.Status == "Cancelled")
        {
            throw new ApplicationException($"Order {command.OrderId} is already cancelled.");
        }

        return EventOrNone.From(
            new OrderCancelled(command.OrderId, command.CancelledAt ?? DateTimeOffset.UtcNow),
            tag);
    }
}
