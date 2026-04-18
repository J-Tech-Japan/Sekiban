using Dcb.Domain.WithoutResult.Order;
using Dcb.Domain.WithoutResult.MaterializedViews;
using Dcb.Domain.WithoutResult.Weather;
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
    [SkippableFact]
    public async Task Initialize_CreatesRegistryAndTables()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres fixture is unavailable.");
        await fixture.ResetAsync();

        await fixture.Executor.InitializeAsync(ApplyHost(fixture.Projector));

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

    [SkippableFact]
    public async Task CatchUp_MaterializesRowsAndIsIdempotent()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres fixture is unavailable.");
        await fixture.ResetAsync();
        await fixture.Executor.InitializeAsync(ApplyHost(fixture.Projector));

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

        var firstCatchUp = await fixture.Executor.CatchUpOnceAsync(ApplyHost(fixture.Projector));
        var secondCatchUp = await fixture.Executor.CatchUpOnceAsync(ApplyHost(fixture.Projector));

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
        var registryRow = await connection.QuerySingleAsync<RegistryQueryRow>(
            """
            SELECT current_position AS CurrentPosition,
                   last_sortable_unique_id AS LastSortableUniqueId,
                   applied_event_version AS AppliedEventVersion,
                   last_applied_source AS LastAppliedSource,
                   last_applied_at AS LastAppliedAt,
                   last_stream_received_sortable_unique_id AS LastStreamReceivedSortableUniqueId,
                   last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId,
                   last_catch_up_sortable_unique_id AS LastCatchUpSortableUniqueId
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
        Assert.Equal(latestSortableUniqueId, registryRow.CurrentPosition);
        Assert.Equal(latestSortableUniqueId, registryRow.LastSortableUniqueId);
        Assert.Equal(3, registryRow.AppliedEventVersion);
        Assert.Equal("catchup", registryRow.LastAppliedSource);
        Assert.NotNull(registryRow.LastAppliedAt);
        Assert.Null(registryRow.LastStreamReceivedSortableUniqueId);
        Assert.Null(registryRow.LastStreamAppliedSortableUniqueId);
        Assert.Equal(latestSortableUniqueId, registryRow.LastCatchUpSortableUniqueId);
        Assert.False(string.IsNullOrWhiteSpace(orderRow.LastSortableUniqueId));
        Assert.NotEqual(default, orderRow.LastAppliedAt);
        Assert.False(string.IsNullOrWhiteSpace(itemRow.LastSortableUniqueId));
        Assert.NotEqual(default, itemRow.LastAppliedAt);
    }

    [SkippableFact]
    public async Task CatchUp_RemainsIdempotentWhenRegistryPositionIsRewound()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres fixture is unavailable.");
        await fixture.ResetAsync();
        await fixture.Executor.InitializeAsync(ApplyHost(fixture.Projector));

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

        await fixture.Executor.CatchUpOnceAsync(ApplyHost(fixture.Projector));

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

        var replayCatchUp = await fixture.Executor.CatchUpOnceAsync(ApplyHost(fixture.Projector));

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

    [SkippableFact]
    public async Task CatchUp_RollsBackWhenApplyStatementFails()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres fixture is unavailable.");
        await fixture.ResetAsync();
        var failingProjector = new FailingMvProjector();
        await fixture.Executor.InitializeAsync(ApplyHost(failingProjector));

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var orderId = Guid.CreateVersion7();
        await executor.ExecuteAsync(new CreateOrder { OrderId = orderId, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        await Assert.ThrowsAnyAsync<PostgresException>(() => fixture.Executor.CatchUpOnceAsync(ApplyHost(failingProjector)));

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

    [SkippableFact]
    public async Task CatchUp_WeatherForecastMaterializesSingleForecastTable()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres fixture is unavailable.");

        await fixture.ResetAsync();
        var projector = new WeatherForecastMvV1();
        await fixture.Executor.InitializeAsync(ApplyHost(projector));

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var forecastId = Guid.CreateVersion7();
        var forecastDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Tokyo",
            Date = forecastDate,
            TemperatureC = 24,
            Summary = "Sunny"
        });
        await executor.ExecuteAsync(new ChangeLocationName
        {
            ForecastId = forecastId,
            NewLocationName = "Osaka"
        });
        await executor.ExecuteAsync(new UpdateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Osaka",
            Date = forecastDate.AddDays(1),
            TemperatureC = 18,
            Summary = "Cloudy"
        });

        var catchUp = await fixture.Executor.CatchUpOnceAsync(ApplyHost(projector));

        await using var connection = await fixture.OpenConnectionAsync();
        var row = await connection.QuerySingleAsync<WeatherForecastQueryRow>(
            """
            SELECT forecast_id AS ForecastId,
                   location AS Location,
                   forecast_date::timestamp AS ForecastDate,
                   temperature_c AS TemperatureC,
                   summary AS Summary,
                   is_deleted AS IsDeleted,
                   _last_sortable_unique_id AS LastSortableUniqueId
            FROM sekiban_mv_weatherforecast_v1_forecasts
            WHERE forecast_id = @ForecastId;
            """,
            new { ForecastId = forecastId });

        Assert.Equal(3, catchUp.AppliedEvents);
        Assert.Equal(forecastId, row.ForecastId);
        Assert.Equal("Osaka", row.Location);
        Assert.Equal(forecastDate.AddDays(1).ToDateTime(TimeOnly.MinValue), row.ForecastDate.Date);
        Assert.Equal(18, row.TemperatureC);
        Assert.Equal("Cloudy", row.Summary);
        Assert.False(row.IsDeleted);
        Assert.False(string.IsNullOrWhiteSpace(row.LastSortableUniqueId));
    }

    [SkippableFact]
    public async Task CatchUp_NativeHost_PreservesConnectionAndTransactionAccess_OnPostgresPath()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres fixture is unavailable.");

        await fixture.ResetAsync();
        var projector = new RawConnectionMvProjector();
        await fixture.Executor.InitializeAsync(ApplyHost(projector));

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var orderId = Guid.CreateVersion7();
        await executor.ExecuteAsync(new CreateOrder { OrderId = orderId, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

        var catchUp = await fixture.Executor.CatchUpOnceAsync(ApplyHost(projector));

        await using var connection = await fixture.OpenConnectionAsync();
        var insertedId = await connection.ExecuteScalarAsync<Guid?>(
            $"SELECT id FROM {RawConnectionMvProjector.TableName} WHERE id = @Id;",
            new { Id = orderId });

        Assert.Equal(1, catchUp.AppliedEvents);
        Assert.Equal(orderId, insertedId);
    }

    private sealed class OrderQueryRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public string LastSortableUniqueId { get; set; } = string.Empty;
        public DateTimeOffset LastAppliedAt { get; set; }
    }

    private sealed class ItemQueryRow
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public string LastSortableUniqueId { get; set; } = string.Empty;
        public DateTimeOffset LastAppliedAt { get; set; }
    }

    private sealed class WeatherForecastQueryRow
    {
        public Guid ForecastId { get; set; }
        public string Location { get; set; } = string.Empty;
        public DateTime ForecastDate { get; set; }
        public int TemperatureC { get; set; }
        public string? Summary { get; set; }
        public bool IsDeleted { get; set; }
        public string LastSortableUniqueId { get; set; } = string.Empty;
    }

    private sealed class RegistryQueryRow
    {
        public string? CurrentPosition { get; set; }
        public string? LastSortableUniqueId { get; set; }
        public long AppliedEventVersion { get; set; }
        public string? LastAppliedSource { get; set; }
        public DateTimeOffset? LastAppliedAt { get; set; }
        public string? LastStreamReceivedSortableUniqueId { get; set; }
        public string? LastStreamAppliedSortableUniqueId { get; set; }
        public string? LastCatchUpSortableUniqueId { get; set; }
    }

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

    private sealed class RawConnectionMvProjector : IMaterializedViewProjector
    {
        public const string TableName = "sekiban_mv_rawconnection_v1_orders";

        public string ViewName => "RawConnection";
        public int ViewVersion => 1;

        public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
        {
            ctx.RegisterTable("orders");
            await ctx.ExecuteAsync(
                $"""
                 CREATE TABLE IF NOT EXISTS {TableName} (
                     id UUID PRIMARY KEY,
                     _last_sortable_unique_id TEXT NOT NULL,
                     _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                 );
                 """,
                cancellationToken: cancellationToken);
        }

        public async Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
            Event ev,
            IMvApplyContext ctx,
            CancellationToken cancellationToken = default)
        {
            if (ev.Payload is not OrderCreated created)
            {
                return [];
            }

            await ctx.Connection.ExecuteAsync(
                new CommandDefinition(
                    $"""
                     INSERT INTO {TableName} (id, _last_sortable_unique_id, _last_applied_at)
                     VALUES (@Id, @SortableUniqueId, NOW())
                     ON CONFLICT (id) DO UPDATE
                     SET _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                         _last_applied_at = EXCLUDED._last_applied_at;
                     """,
                    new { Id = created.OrderId, SortableUniqueId = ctx.CurrentSortableUniqueId },
                    ctx.Transaction,
                    cancellationToken: cancellationToken));

            return [];
        }
    }

    private NativeMvApplyHost ApplyHost(IMaterializedViewProjector projector) =>
        new(projector, fixture.DomainTypes.EventTypes);
}
