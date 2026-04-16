using Dcb.Domain.WithoutResult.Order;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;

namespace Dcb.Domain.WithoutResult.MaterializedViews;

public sealed class OrderSummaryMvV1 : IMaterializedViewProjector
{
    public string ViewName => "OrderSummary";
    public int ViewVersion => 1;

    public MvTable Orders { get; private set; } = default!;
    public MvTable Items { get; private set; } = default!;

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        Orders = ctx.RegisterTable("orders");
        Items = ctx.RegisterTable("items");

        await ctx.ExecuteAsync(
            $"""
             CREATE TABLE IF NOT EXISTS {Orders.PhysicalName} (
                 id UUID PRIMARY KEY,
                 status TEXT NOT NULL,
                 total NUMERIC NOT NULL DEFAULT 0,
                 created_at TIMESTAMPTZ NOT NULL,
                 _last_sortable_unique_id TEXT NOT NULL,
                 _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
             );
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ctx.ExecuteAsync(
            $"""
             CREATE TABLE IF NOT EXISTS {Items.PhysicalName} (
                 id UUID PRIMARY KEY,
                 order_id UUID NOT NULL REFERENCES {Orders.PhysicalName}(id),
                 product_name TEXT NOT NULL,
                 quantity INT NOT NULL,
                 unit_price NUMERIC NOT NULL,
                 _last_sortable_unique_id TEXT NOT NULL,
                 _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
             );
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ctx.ExecuteAsync(
            $"""
             CREATE INDEX IF NOT EXISTS idx_{Items.PhysicalName}_order_id
             ON {Items.PhysicalName} (order_id);
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default)
    {
        return ev.Payload switch
        {
            OrderCreated created => [UpsertOrder(created, ctx.CurrentSortableUniqueId)],
            OrderItemAdded added => await ApplyItemAddedAsync(added, ctx, cancellationToken).ConfigureAwait(false),
            OrderCancelled cancelled => [CancelOrder(cancelled, ctx.CurrentSortableUniqueId)],
            _ => []
        };
    }

    private async Task<IReadOnlyList<MvSqlStatement>> ApplyItemAddedAsync(
        OrderItemAdded added,
        IMvApplyContext ctx,
        CancellationToken cancellationToken)
    {
        var orderExists = await ctx.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM {Orders.PhysicalName} WHERE id = @Id",
            new { Id = added.OrderId },
            cancellationToken).ConfigureAwait(false);

        if (orderExists == 0)
        {
            return [];
        }

        return
        [
            InsertItem(added, ctx.CurrentSortableUniqueId),
            UpdateOrderTotal(added.OrderId, added.Quantity * added.UnitPrice, ctx.CurrentSortableUniqueId)
        ];
    }

    private MvSqlStatement UpsertOrder(OrderCreated created, string sortableUniqueId) =>
        new(
            $"""
             INSERT INTO {Orders.PhysicalName}
                 (id, status, total, created_at, _last_sortable_unique_id, _last_applied_at)
             VALUES
                 (@OrderId, @Status, @Total, @CreatedAt, @SortableUniqueId, NOW())
             ON CONFLICT (id) DO UPDATE SET
                 status = EXCLUDED.status,
                 total = EXCLUDED.total,
                 created_at = EXCLUDED.created_at,
                 _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                 _last_applied_at = EXCLUDED._last_applied_at
             WHERE {Orders.PhysicalName}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
             """,
            new
            {
                created.OrderId,
                Status = "Pending",
                Total = 0m,
                created.CreatedAt,
                SortableUniqueId = sortableUniqueId
            });

    private MvSqlStatement InsertItem(OrderItemAdded added, string sortableUniqueId) =>
        new(
            $"""
             INSERT INTO {Items.PhysicalName}
                 (id, order_id, product_name, quantity, unit_price, _last_sortable_unique_id, _last_applied_at)
             VALUES
                 (@ItemId, @OrderId, @ProductName, @Quantity, @UnitPrice, @SortableUniqueId, NOW())
             ON CONFLICT (id) DO UPDATE SET
                 order_id = EXCLUDED.order_id,
                 product_name = EXCLUDED.product_name,
                 quantity = EXCLUDED.quantity,
                 unit_price = EXCLUDED.unit_price,
                 _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                 _last_applied_at = EXCLUDED._last_applied_at
             WHERE {Items.PhysicalName}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
             """,
            new
            {
                added.ItemId,
                added.OrderId,
                added.ProductName,
                added.Quantity,
                added.UnitPrice,
                SortableUniqueId = sortableUniqueId
            });

    private MvSqlStatement UpdateOrderTotal(Guid orderId, decimal delta, string sortableUniqueId) =>
        new(
            $"""
             UPDATE {Orders.PhysicalName}
             SET total = total + @Delta,
                 _last_sortable_unique_id = @SortableUniqueId,
                 _last_applied_at = NOW()
             WHERE id = @OrderId
               AND _last_sortable_unique_id < @SortableUniqueId;
             """,
            new
            {
                OrderId = orderId,
                Delta = delta,
                SortableUniqueId = sortableUniqueId
            });

    private MvSqlStatement CancelOrder(OrderCancelled cancelled, string sortableUniqueId) =>
        new(
            $"""
             UPDATE {Orders.PhysicalName}
             SET status = @Status,
                 _last_sortable_unique_id = @SortableUniqueId,
                 _last_applied_at = NOW()
             WHERE id = @OrderId
               AND _last_sortable_unique_id < @SortableUniqueId;
             """,
            new
            {
                cancelled.OrderId,
                Status = "Cancelled",
                SortableUniqueId = sortableUniqueId
            });
}

public sealed record OrderRow(
    [property: MvColumn("id")] Guid Id,
    [property: MvColumn("status")] string Status,
    [property: MvColumn("total")] decimal Total,
    [property: MvColumn("created_at")] DateTimeOffset CreatedAt,
    [property: MvColumn("_last_sortable_unique_id")] string LastSortableUniqueId,
    [property: MvColumn("_last_applied_at")] DateTimeOffset LastAppliedAt);

public sealed record OrderItemRow(
    [property: MvColumn("id")] Guid Id,
    [property: MvColumn("order_id")] Guid OrderId,
    [property: MvColumn("product_name")] string ProductName,
    [property: MvColumn("quantity")] int Quantity,
    [property: MvColumn("unit_price")] decimal UnitPrice,
    [property: MvColumn("_last_sortable_unique_id")] string LastSortableUniqueId,
    [property: MvColumn("_last_applied_at")] DateTimeOffset LastAppliedAt);
