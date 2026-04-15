using Sekiban.Dcb.Events;

namespace Dcb.Domain.WithoutResult.Order;

public sealed record OrderCreated(Guid OrderId, DateTimeOffset CreatedAt) : IEventPayload;

public sealed record OrderItemAdded(
    Guid OrderId,
    Guid ItemId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset AddedAt) : IEventPayload;

public sealed record OrderCancelled(Guid OrderId, DateTimeOffset CancelledAt) : IEventPayload;
