using Dcb.Domain.WithoutResult.Order;
using Dapper;
using Npgsql;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Postgres.Tests;

[CollectionDefinition(nameof(MaterializedViewPostgresCollection))]
public sealed class MaterializedViewPostgresCollection : ICollectionFixture<MaterializedViewPostgresFixture>;

[Collection(nameof(MaterializedViewPostgresCollection))]
public sealed class MaterializedViewPostgresTests(MaterializedViewPostgresFixture fixture)
{
    [Fact]
    public async Task Initialize_CreatesRegistryAndTables()
    {
        await fixture.ResetAsync();

        await fixture.Executor.InitializeAsync(fixture.Projector);

        await using var connection = await fixture.OpenConnectionAsync();
        var registryCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sekiban_mv_registry;");
        var activeVersion = await connection.ExecuteScalarAsync<int>("SELECT active_version FROM sekiban_mv_active WHERE view_name = 'OrderSummary';");
        var ordersExists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'sekiban_mv_ordersummary_v1_orders');");
        var itemsExists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'sekiban_mv_ordersummary_v1_items');");

        Assert.Equal(2, registryCount);
        Assert.Equal(1, activeVersion);
        Assert.True(ordersExists);
        Assert.True(itemsExists);
    }

    [Fact]
    public async Task CatchUp_MaterializesRowsAndIsIdempotent()
    {
        await fixture.ResetAsync();
        await fixture.Executor.InitializeAsync(fixture.Projector);

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var orderId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();

        await executor.ExecuteAsync(new CreateOrder { OrderId = orderId, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await executor.ExecuteAsync(new AddOrderItem
        {
            OrderId = orderId,
            ItemId = itemId,
            ProductName = "Mouse",
            Quantity = 2,
            UnitPrice = 15m,
            AddedAt = DateTimeOffset.UtcNow.AddSeconds(-30)
        });
        await executor.ExecuteAsync(new CancelOrder { OrderId = orderId, CancelledAt = DateTimeOffset.UtcNow.AddSeconds(-10) });

        var firstCatchUp = await fixture.Executor.CatchUpOnceAsync(fixture.Projector);
        var secondCatchUp = await fixture.Executor.CatchUpOnceAsync(fixture.Projector);

        await using var connection = await fixture.OpenConnectionAsync();
        var orderRow = await connection.QuerySingleAsync<OrderQueryRow>(
            """
            SELECT id,
                   status,
                   total,
                   _last_sortable_unique_id AS LastSortableUniqueId,
                   _last_applied_at AS LastAppliedAt
            FROM sekiban_mv_ordersummary_v1_orders
            WHERE id = @Id;
            """,
            new { Id = orderId });
        var itemRow = await connection.QuerySingleAsync<ItemQueryRow>(
            """
            SELECT id,
                   order_id AS OrderId,
                   _last_sortable_unique_id AS LastSortableUniqueId,
                   _last_applied_at AS LastAppliedAt
            FROM sekiban_mv_ordersummary_v1_items
            WHERE order_id = @Id;
            """,
            new { Id = orderId });
        var itemCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sekiban_mv_ordersummary_v1_items WHERE order_id = @Id;",
            new { Id = orderId });
        var registryPosition = await connection.ExecuteScalarAsync<string>(
            """
            SELECT current_position
            FROM sekiban_mv_registry
            WHERE view_name = 'OrderSummary' AND logical_table = 'orders';
            """);

        var latestSortableUniqueId = (await fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue()
            .Max(serializableEvent => serializableEvent.SortableUniqueIdValue);

        Assert.Equal(3, firstCatchUp.AppliedEvents);
        Assert.Equal(0, secondCatchUp.AppliedEvents);
        Assert.Equal(orderId, orderRow.Id);
        Assert.Equal("Cancelled", orderRow.Status);
        Assert.Equal(30m, orderRow.Total);
        Assert.Equal(1, itemCount);
        Assert.Equal(latestSortableUniqueId, registryPosition);
        Assert.False(string.IsNullOrWhiteSpace(orderRow.LastSortableUniqueId));
        Assert.NotEqual(default, orderRow.LastAppliedAt);
        Assert.False(string.IsNullOrWhiteSpace(itemRow.LastSortableUniqueId));
        Assert.NotEqual(default, itemRow.LastAppliedAt);
    }

    [Fact]
    public async Task CatchUp_RemainsIdempotentWhenRegistryPositionIsRewound()
    {
        await fixture.ResetAsync();
        await fixture.Executor.InitializeAsync(fixture.Projector);

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var orderId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();

        await executor.ExecuteAsync(new CreateOrder { OrderId = orderId, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        await executor.ExecuteAsync(new AddOrderItem
        {
            OrderId = orderId,
            ItemId = itemId,
            ProductName = "Mouse",
            Quantity = 2,
            UnitPrice = 15m,
            AddedAt = DateTimeOffset.UtcNow.AddSeconds(-30)
        });
        await executor.ExecuteAsync(new CancelOrder { OrderId = orderId, CancelledAt = DateTimeOffset.UtcNow.AddSeconds(-10) });

        await fixture.Executor.CatchUpOnceAsync(fixture.Projector);

        await using (var rewindConnection = await fixture.OpenConnectionAsync())
        {
            await rewindConnection.ExecuteAsync(
                """
                UPDATE sekiban_mv_registry
                SET current_position = NULL,
                    last_sortable_unique_id = NULL
                WHERE view_name = 'OrderSummary';
                """);
        }

        var replayCatchUp = await fixture.Executor.CatchUpOnceAsync(fixture.Projector);

        await using var connection = await fixture.OpenConnectionAsync();
        var orderRow = await connection.QuerySingleAsync<OrderQueryRow>(
            """
            SELECT id,
                   status,
                   total,
                   _last_sortable_unique_id AS LastSortableUniqueId,
                   _last_applied_at AS LastAppliedAt
            FROM sekiban_mv_ordersummary_v1_orders
            WHERE id = @Id;
            """,
            new { Id = orderId });
        var itemCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sekiban_mv_ordersummary_v1_items WHERE order_id = @Id;",
            new { Id = orderId });

        Assert.Equal(3, replayCatchUp.AppliedEvents);
        Assert.Equal("Cancelled", orderRow.Status);
        Assert.Equal(30m, orderRow.Total);
        Assert.Equal(1, itemCount);
        Assert.NotEqual(default, orderRow.LastAppliedAt);
    }

    [Fact]
    public async Task CatchUp_RollsBackWhenApplyStatementFails()
    {
        await fixture.ResetAsync();
        var failingProjector = new FailingMvProjector();
        await fixture.Executor.InitializeAsync(failingProjector);

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var orderId = Guid.CreateVersion7();
        await executor.ExecuteAsync(new CreateOrder { OrderId = orderId, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        await Assert.ThrowsAnyAsync<PostgresException>(() => fixture.Executor.CatchUpOnceAsync(failingProjector));

        await using var connection = await fixture.OpenConnectionAsync();
        var rowCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sekiban_mv_failure_v1_orders;");
        var registryPosition = await connection.QuerySingleOrDefaultAsync<string?>(
            """
            SELECT current_position
            FROM sekiban_mv_registry
            WHERE view_name = 'Failure' AND logical_table = 'orders';
            """);

        Assert.Equal(0, rowCount);
        Assert.Null(registryPosition);
    }

    private sealed record OrderQueryRow(Guid Id, string Status, decimal Total, string LastSortableUniqueId, DateTimeOffset LastAppliedAt);
    private sealed record ItemQueryRow(Guid Id, Guid OrderId, string LastSortableUniqueId, DateTimeOffset LastAppliedAt);

    private sealed class FailingMvProjector : IMaterializedViewProjector
    {
        public string ViewName => "Failure";
        public int ViewVersion => 1;
        private MvTable Orders { get; set; } = default!;

        public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
        {
            Orders = ctx.RegisterTable("orders");
            await ctx.ExecuteAsync(
                $"""
                 CREATE TABLE IF NOT EXISTS {Orders.PhysicalName} (
                     id UUID PRIMARY KEY,
                     _last_sortable_unique_id TEXT NOT NULL,
                     _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                 );
                 """,
                cancellationToken: cancellationToken);
        }

        public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(Event ev, IMvApplyContext ctx, CancellationToken cancellationToken = default)
        {
            if (ev.Payload is not OrderCreated created)
            {
                return Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
            }

            return Task.FromResult<IReadOnlyList<MvSqlStatement>>(
            [
                new MvSqlStatement(
                    $"""
                     INSERT INTO {Orders.PhysicalName} (id, _last_sortable_unique_id, _last_applied_at)
                     VALUES (@Id, @SortableUniqueId, NOW());
                     """,
                    new { Id = created.OrderId, SortableUniqueId = ctx.CurrentSortableUniqueId }),
                new MvSqlStatement("INSERT INTO definitely_missing_table(id) VALUES (1);")
            ]);
        }
    }
}
