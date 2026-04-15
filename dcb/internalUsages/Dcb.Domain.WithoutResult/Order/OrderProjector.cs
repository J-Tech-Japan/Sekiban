using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Dcb.Domain.WithoutResult.Order;

public sealed class OrderProjector : ITagProjector<OrderProjector>
{
    public static string ProjectorVersion => "1.0.0";
    public static string ProjectorName => nameof(OrderProjector);

    public static ITagStatePayload Project(ITagStatePayload current, Event ev)
    {
        var state = current as OrderState ?? new OrderState();
        return ev.Payload switch
        {
            OrderCreated created => state with
            {
                OrderId = created.OrderId,
                CreatedAt = created.CreatedAt,
                Status = "Pending",
                TotalAmount = 0m
            },
            OrderItemAdded added => state with
            {
                TotalAmount = state.TotalAmount + (added.Quantity * added.UnitPrice)
            },
            OrderCancelled => state with
            {
                Status = "Cancelled"
            },
            _ => state
        };
    }
}
