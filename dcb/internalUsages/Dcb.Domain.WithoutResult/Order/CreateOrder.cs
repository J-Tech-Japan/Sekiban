using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;

namespace Dcb.Domain.WithoutResult.Order;

public sealed record CreateOrder : ICommandWithHandler<CreateOrder>
{
    public Guid OrderId { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public static async Task<EventOrNone> HandleAsync(CreateOrder command, ICommandContext context)
    {
        var orderId = command.OrderId == Guid.Empty ? Guid.CreateVersion7() : command.OrderId;
        var tag = new OrderTag(orderId);
        if (await context.TagExistsAsync(tag).ConfigureAwait(false))
        {
            throw new OrderCommandException($"Order {orderId} already exists.");
        }

        return EventOrNone.From(
            new OrderCreated(orderId, command.CreatedAt ?? DateTimeOffset.UtcNow),
            tag);
    }
}
