using Sekiban.Dcb.Tags;

namespace Dcb.Domain.WithoutResult.Order;

public record OrderTag(Guid OrderId) : IGuidTagGroup<OrderTag>
{
    public static string TagGroupName => "Order";

    public bool IsConsistencyTag() => true;

    public static OrderTag FromContent(string content)
    {
        if (Guid.TryParse(content, out var orderId))
        {
            return new OrderTag(orderId);
        }

        throw new ArgumentException($"Invalid order ID format: {content}");
    }

    public Guid GetId() => OrderId;
}
