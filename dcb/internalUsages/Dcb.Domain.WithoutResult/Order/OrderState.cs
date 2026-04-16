using Orleans;
using Sekiban.Dcb.Tags;

namespace Dcb.Domain.WithoutResult.Order;

public record OrderState : ITagStatePayload
{
    [Id(0)] public Guid OrderId { get; init; }
    [Id(1)] public string Status { get; init; } = "Pending";
    [Id(2)] public DateTimeOffset CreatedAt { get; init; }
    [Id(3)] public decimal TotalAmount { get; init; }
}
