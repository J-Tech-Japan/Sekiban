using Dapper;
using Dcb.Domain.WithoutResult.Weather;
using Dcb.Domain.WithoutResult.Order;
using Orleans.Streams;
using Orleans.TestingHost;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView.Orleans;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.ServiceId;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Postgres.Tests;

[CollectionDefinition(nameof(MaterializedViewPostgresOrleansCollection))]
public sealed class MaterializedViewPostgresOrleansCollection : ICollectionFixture<MaterializedViewPostgresOrleansFixture>;

[Collection(nameof(MaterializedViewPostgresOrleansCollection))]
public sealed class MaterializedViewPostgresOrleansTests(MaterializedViewPostgresOrleansFixture fixture)
{
    [Fact]
    public async Task Grain_CatchesUp_Then_Applies_Streamed_Event_To_Postgres_View()
    {
        if (!fixture.IsAvailable)
        {
            fixture.EnsureAvailable();
            return;
        }

        var grainKey = MvGrainKey.Build(DefaultServiceIdProvider.DefaultServiceId, "OrderSummary", 1);
        var grain = fixture.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        try
        {
            await grain.RequestDeactivationAsync();
            await Task.Delay(200);
        }
        catch
        {
            // The grain may not be active yet; that's fine for this reset path.
        }

        await fixture.ResetAsync();

        var catchUpExecutor = fixture.CreateExecutor(publishToStream: false);
        var streamingExecutor = fixture.CreateExecutor(publishToStream: true);

        var orderId = Guid.CreateVersion7();
        var firstItemId = Guid.CreateVersion7();
        var secondItemId = Guid.CreateVersion7();

        await catchUpExecutor.ExecuteAsync(new CreateOrder
        {
            OrderId = orderId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        });
        await catchUpExecutor.ExecuteAsync(new AddOrderItem
        {
            OrderId = orderId,
            ItemId = firstItemId,
            ProductName = "Mouse",
            Quantity = 2,
            UnitPrice = 15m,
            AddedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        await grain.EnsureStartedAsync();

        var latestBeforeStream = await GetLatestSortableUniqueIdAsync();
        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            if (status.CurrentPosition != latestBeforeStream)
            {
                return false;
            }

            await using var connection = await fixture.OpenConnectionAsync();
            var total = await connection.ExecuteScalarAsync<decimal?>(
                "SELECT total FROM sekiban_mv_ordersummary_v1_orders WHERE id = @Id;",
                new { Id = orderId });
            var itemCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sekiban_mv_ordersummary_v1_items WHERE order_id = @Id;",
                new { Id = orderId });
            return total == 30m && itemCount == 1;
        });

        await streamingExecutor.ExecuteAsync(new AddOrderItem
        {
            OrderId = orderId,
            ItemId = secondItemId,
            ProductName = "Keyboard",
            Quantity = 1,
            UnitPrice = 5m,
            AddedAt = DateTimeOffset.UtcNow
        });

        var latestAfterStream = await GetLatestSortableUniqueIdAsync();
        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            if (status.CurrentPosition != latestAfterStream)
            {
                return false;
            }

            await using var connection = await fixture.OpenConnectionAsync();
            var total = await connection.ExecuteScalarAsync<decimal?>(
                "SELECT total FROM sekiban_mv_ordersummary_v1_orders WHERE id = @Id;",
                new { Id = orderId });
            var itemCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sekiban_mv_ordersummary_v1_items WHERE order_id = @Id;",
                new { Id = orderId });
            return total == 35m && itemCount == 2;
        }, timeoutMs: 15000);

        await using var verifyConnection = await fixture.OpenConnectionAsync();
        var orderRow = await verifyConnection.QuerySingleAsync<OrderProjectionRow>(
            """
            SELECT id,
                   status,
                   total,
                   _last_sortable_unique_id AS LastSortableUniqueId
            FROM sekiban_mv_ordersummary_v1_orders
            WHERE id = @Id;
            """,
            new { Id = orderId });
        var registryRow = await verifyConnection.QuerySingleAsync<RegistryProjectionRow>(
            """
            SELECT current_position AS CurrentPosition,
                   last_sortable_unique_id AS LastSortableUniqueId,
                   applied_event_version AS AppliedEventVersion,
                   last_applied_source AS LastAppliedSource,
                   last_applied_at AS LastAppliedAt,
                   last_stream_received_sortable_unique_id AS LastStreamReceivedSortableUniqueId,
                   last_stream_received_at AS LastStreamReceivedAt,
                   last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId,
                   last_catch_up_sortable_unique_id AS LastCatchUpSortableUniqueId
            FROM sekiban_mv_registry
            WHERE view_name = 'OrderSummary' AND logical_table = 'orders';
            """);

        Assert.Equal(orderId, orderRow.Id);
        Assert.Equal("Pending", orderRow.Status);
        Assert.Equal(35m, orderRow.Total);
        Assert.Equal(latestAfterStream, registryRow.CurrentPosition);
        Assert.Equal(latestAfterStream, registryRow.LastSortableUniqueId);
        Assert.Equal(3, registryRow.AppliedEventVersion);
        Assert.Equal("stream", registryRow.LastAppliedSource);
        Assert.NotNull(registryRow.LastAppliedAt);
        Assert.Equal(latestAfterStream, registryRow.LastStreamReceivedSortableUniqueId);
        Assert.NotNull(registryRow.LastStreamReceivedAt);
        Assert.Equal(latestAfterStream, registryRow.LastStreamAppliedSortableUniqueId);
        Assert.Equal(latestBeforeStream, registryRow.LastCatchUpSortableUniqueId);
        Assert.Equal(latestAfterStream, orderRow.LastSortableUniqueId);

        async Task<string> GetLatestSortableUniqueIdAsync()
        {
            var result = await fixture.EventStore.ReadAllSerializableEventsAsync();
            return result.GetValue()
                .OrderByDescending(static serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
                .Select(static serializableEvent => serializableEvent.SortableUniqueIdValue)
                .FirstOrDefault() ?? throw new InvalidOperationException("No events found in event store.");
        }
    }

    [Fact]
    public async Task Grain_OutOfOrder_StreamDelivery_DoesNotLose_WeatherForecastRows()
    {
        if (!fixture.IsAvailable)
        {
            fixture.EnsureAvailable();
            return;
        }

        var grainKey = MvGrainKey.Build(DefaultServiceIdProvider.DefaultServiceId, "WeatherForecast", 1);
        var grain = fixture.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        try
        {
            await grain.RequestDeactivationAsync();
            await Task.Delay(200);
        }
        catch
        {
            // The grain may not be active yet; that's fine for this reset path.
        }

        await fixture.ResetAsync();
        await grain.EnsureStartedAsync();

        var executor = fixture.CreateExecutor(publishToStream: false);
        const int forecastCount = 64;

        for (var index = 0; index < forecastCount; index++)
        {
            var forecastId = Guid.CreateVersion7();
            await executor.ExecuteAsync(new CreateWeatherForecast
            {
                ForecastId = forecastId,
                Location = $"Loc-{index:D3}",
                Date = new DateOnly(2026, 4, 15).AddDays(index % 7),
                TemperatureC = 20 + (index % 10),
                Summary = $"Forecast-{index:D3}"
            });
            await executor.ExecuteAsync(new ChangeLocationName
            {
                ForecastId = forecastId,
                NewLocationName = $"Loc-{index:D3}-U"
            });
        }

        var readResult = await fixture.EventStore.ReadAllSerializableEventsAsync();
        var allEvents = readResult.GetValue()
            .OrderByDescending(static serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();
        var latestSortableUniqueId = allEvents
            .OrderByDescending(static serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
            .Select(static serializableEvent => serializableEvent.SortableUniqueIdValue)
            .First();

        var streamNamespace = ServiceIdGrainKey.BuildStreamNamespace("AllEvents", DefaultServiceIdProvider.DefaultServiceId);
        var stream = fixture.Client
            .GetStreamProvider("EventStreamProvider")
            .GetStream<SerializableEvent>(StreamId.Create(streamNamespace, Guid.Empty));

        foreach (var serializableEvent in allEvents)
        {
            await stream.OnNextAsync(serializableEvent);
        }

        MaterializedViewGrainStatus? lastStatus = null;
        var lastRowCount = -1;
        var lastUpdatedLocationCount = -1;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            lastStatus = await grain.GetStatusAsync();
            await using var connection = await fixture.OpenConnectionAsync();
            lastRowCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sekiban_mv_weatherforecast_v1_forecasts WHERE is_deleted = FALSE;");
            lastUpdatedLocationCount = await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sekiban_mv_weatherforecast_v1_forecasts
                WHERE is_deleted = FALSE
                  AND location LIKE '%-U';
                """);
            if (lastStatus.CurrentPosition == latestSortableUniqueId &&
                lastRowCount == forecastCount &&
                lastUpdatedLocationCount == forecastCount)
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.True(
            lastStatus?.CurrentPosition == latestSortableUniqueId &&
            lastRowCount == forecastCount &&
            lastUpdatedLocationCount == forecastCount,
            $"Expected position={latestSortableUniqueId}, rows={forecastCount}, updatedRows={forecastCount} but got position={lastStatus?.CurrentPosition}, rows={lastRowCount}, updatedRows={lastUpdatedLocationCount}.");

        await using var verifyConnection = await fixture.OpenConnectionAsync();
        var rowCount = await verifyConnection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sekiban_mv_weatherforecast_v1_forecasts WHERE is_deleted = FALSE;");
        var missingCount = await verifyConnection.ExecuteScalarAsync<int>(
            """
            WITH created_ids AS (
                SELECT DISTINCT "Payload"->>'forecastId' AS forecast_id
                FROM dcb_events
                WHERE "EventType" = 'WeatherForecastCreated'
            )
            SELECT COUNT(*)
            FROM created_ids created
            LEFT JOIN sekiban_mv_weatherforecast_v1_forecasts mv
              ON mv.forecast_id::text = created.forecast_id
            WHERE mv.forecast_id IS NULL;
            """);
        var staleLocationCount = await verifyConnection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sekiban_mv_weatherforecast_v1_forecasts
            WHERE is_deleted = FALSE
              AND location NOT LIKE '%-U';
            """);
        var registryRow = await verifyConnection.QuerySingleAsync<RegistryProjectionRow>(
            """
            SELECT current_position AS CurrentPosition,
                   last_sortable_unique_id AS LastSortableUniqueId,
                   applied_event_version AS AppliedEventVersion,
                   last_applied_source AS LastAppliedSource,
                   last_applied_at AS LastAppliedAt,
                   last_stream_received_sortable_unique_id AS LastStreamReceivedSortableUniqueId,
                   last_stream_received_at AS LastStreamReceivedAt,
                   last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId,
                   last_catch_up_sortable_unique_id AS LastCatchUpSortableUniqueId
            FROM sekiban_mv_registry
            WHERE view_name = 'WeatherForecast' AND logical_table = 'forecasts';
            """);

        Assert.Equal(forecastCount, rowCount);
        Assert.Equal(0, missingCount);
        Assert.Equal(0, staleLocationCount);
        Assert.Equal(latestSortableUniqueId, registryRow.CurrentPosition);
        Assert.Equal(latestSortableUniqueId, registryRow.LastSortableUniqueId);
        Assert.Equal(forecastCount * 2, registryRow.AppliedEventVersion);
        Assert.Equal("stream", registryRow.LastAppliedSource);
        Assert.NotNull(registryRow.LastAppliedAt);
        Assert.Equal(latestSortableUniqueId, registryRow.LastStreamReceivedSortableUniqueId);
        Assert.NotNull(registryRow.LastStreamReceivedAt);
        Assert.Equal(latestSortableUniqueId, registryRow.LastStreamAppliedSortableUniqueId);
        Assert.Null(registryRow.LastCatchUpSortableUniqueId);
    }

    [Fact]
    public async Task Grain_Delayed_Create_After_Streamed_Update_DoesNotAdvance_Past_Missing_Row()
    {
        if (!fixture.IsAvailable)
        {
            fixture.EnsureAvailable();
            return;
        }

        var grainKey = MvGrainKey.Build(DefaultServiceIdProvider.DefaultServiceId, "WeatherForecast", 1);
        var grain = fixture.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        try
        {
            await grain.RequestDeactivationAsync();
            await Task.Delay(200);
        }
        catch
        {
            // The grain may not be active yet; that's fine for this reset path.
        }

        await fixture.ResetAsync();
        await grain.EnsureStartedAsync();

        var executor = fixture.CreateExecutor(publishToStream: false);
        var forecastId = Guid.CreateVersion7();
        await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Loc-delayed",
            Date = new DateOnly(2026, 4, 16),
            TemperatureC = 23,
            Summary = "Delayed create"
        });
        await executor.ExecuteAsync(new ChangeLocationName
        {
            ForecastId = forecastId,
            NewLocationName = "Loc-delayed-U"
        });

        var events = (await fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue()
            .Where(serializableEvent =>
            {
                var eventResult = serializableEvent.ToEvent(fixture.DomainTypes.EventTypes);
                if (!eventResult.IsSuccess)
                {
                    return false;
                }

                return eventResult.GetValue().Payload switch
                {
                    WeatherForecastCreated created => created.ForecastId == forecastId,
                    LocationNameChanged changed => changed.ForecastId == forecastId,
                    _ => false
                };
            })
            .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, events.Count);

        var createEvent = events[0];
        var updateEvent = events[1];

        var streamNamespace = ServiceIdGrainKey.BuildStreamNamespace("AllEvents", DefaultServiceIdProvider.DefaultServiceId);
        var stream = fixture.Client
            .GetStreamProvider("EventStreamProvider")
            .GetStream<SerializableEvent>(StreamId.Create(streamNamespace, Guid.Empty));

        await stream.OnNextAsync(updateEvent);
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        var statusAfterUpdateOnly = await grain.GetStatusAsync();
        await using (var interimConnection = await fixture.OpenConnectionAsync())
        {
            var interimCount = await interimConnection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sekiban_mv_weatherforecast_v1_forecasts WHERE forecast_id = @ForecastId;",
                new { ForecastId = forecastId });
            Assert.Equal(0, interimCount);
        }
        Assert.True(
            string.IsNullOrWhiteSpace(statusAfterUpdateOnly.CurrentPosition) ||
            string.Compare(statusAfterUpdateOnly.CurrentPosition, updateEvent.SortableUniqueIdValue, StringComparison.Ordinal) < 0);

        await stream.OnNextAsync(createEvent);

        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            if (status.CurrentPosition != updateEvent.SortableUniqueIdValue)
            {
                return false;
            }

            await using var connection = await fixture.OpenConnectionAsync();
            var row = await connection.QuerySingleOrDefaultAsync<WeatherProjectionRow>(
                """
                SELECT forecast_id AS ForecastId,
                       location AS Location,
                       _last_sortable_unique_id AS LastSortableUniqueId
                FROM sekiban_mv_weatherforecast_v1_forecasts
                WHERE forecast_id = @ForecastId;
                """,
                new { ForecastId = forecastId });

            return row is not null &&
                   row.Location == "Loc-delayed-U" &&
                   row.LastSortableUniqueId == updateEvent.SortableUniqueIdValue;
        }, timeoutMs: 15000);

        await using var verifyConnection = await fixture.OpenConnectionAsync();
        var registryRow = await verifyConnection.QuerySingleAsync<RegistryProjectionRow>(
            """
            SELECT current_position AS CurrentPosition,
                   last_sortable_unique_id AS LastSortableUniqueId,
                   applied_event_version AS AppliedEventVersion,
                   last_applied_source AS LastAppliedSource,
                   last_applied_at AS LastAppliedAt,
                   last_stream_received_sortable_unique_id AS LastStreamReceivedSortableUniqueId,
                   last_stream_received_at AS LastStreamReceivedAt,
                   last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId,
                   last_catch_up_sortable_unique_id AS LastCatchUpSortableUniqueId
            FROM sekiban_mv_registry
            WHERE view_name = 'WeatherForecast' AND logical_table = 'forecasts';
            """);

        var rowCount = await verifyConnection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sekiban_mv_weatherforecast_v1_forecasts WHERE forecast_id = @ForecastId;",
            new { ForecastId = forecastId });
        var updatedLocation = await verifyConnection.ExecuteScalarAsync<string>(
            "SELECT location FROM sekiban_mv_weatherforecast_v1_forecasts WHERE forecast_id = @ForecastId;",
            new { ForecastId = forecastId });

        Assert.Equal(1, rowCount);
        Assert.Equal("Loc-delayed-U", updatedLocation);
        Assert.Equal(updateEvent.SortableUniqueIdValue, registryRow.CurrentPosition);
        Assert.Equal(2, registryRow.AppliedEventVersion);
        Assert.Equal(updateEvent.SortableUniqueIdValue, registryRow.LastStreamAppliedSortableUniqueId);
    }

    [Fact]
    public async Task Grain_Streamed_Create_Then_Update_For_Same_Aggregate_Applies_Both_Events()
    {
        if (!fixture.IsAvailable)
        {
            fixture.EnsureAvailable();
            return;
        }

        var grainKey = MvGrainKey.Build(DefaultServiceIdProvider.DefaultServiceId, "WeatherForecast", 1);
        var grain = fixture.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        try
        {
            await grain.RequestDeactivationAsync();
            await Task.Delay(200);
        }
        catch
        {
            // The grain may not be active yet; that's fine for this reset path.
        }

        await fixture.ResetAsync();
        await grain.EnsureStartedAsync();

        var executor = fixture.CreateExecutor(publishToStream: false);
        var forecastId = Guid.CreateVersion7();
        await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Loc-buffered",
            Date = new DateOnly(2026, 4, 16),
            TemperatureC = 25,
            Summary = "Buffered create"
        });
        await executor.ExecuteAsync(new ChangeLocationName
        {
            ForecastId = forecastId,
            NewLocationName = "Loc-buffered-U"
        });

        var events = (await fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue()
            .Where(serializableEvent =>
            {
                var eventResult = serializableEvent.ToEvent(fixture.DomainTypes.EventTypes);
                if (!eventResult.IsSuccess)
                {
                    return false;
                }

                return eventResult.GetValue().Payload switch
                {
                    WeatherForecastCreated created => created.ForecastId == forecastId,
                    LocationNameChanged changed => changed.ForecastId == forecastId,
                    _ => false
                };
            })
            .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, events.Count);

        var createEvent = events[0];
        var updateEvent = events[1];
        var streamNamespace = ServiceIdGrainKey.BuildStreamNamespace("AllEvents", DefaultServiceIdProvider.DefaultServiceId);
        var stream = fixture.Client
            .GetStreamProvider("EventStreamProvider")
            .GetStream<SerializableEvent>(StreamId.Create(streamNamespace, Guid.Empty));

        await stream.OnNextAsync(createEvent);
        await stream.OnNextAsync(updateEvent);

        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            if (status.CurrentPosition != updateEvent.SortableUniqueIdValue)
            {
                return false;
            }

            await using var connection = await fixture.OpenConnectionAsync();
            var row = await connection.QuerySingleOrDefaultAsync<WeatherProjectionRow>(
                """
                SELECT forecast_id AS ForecastId,
                       location AS Location,
                       _last_sortable_unique_id AS LastSortableUniqueId
                FROM sekiban_mv_weatherforecast_v1_forecasts
                WHERE forecast_id = @ForecastId;
                """,
                new { ForecastId = forecastId });

            return row is not null &&
                   row.Location == "Loc-buffered-U" &&
                   row.LastSortableUniqueId == updateEvent.SortableUniqueIdValue;
        }, timeoutMs: 15000);

        await using var verifyConnection = await fixture.OpenConnectionAsync();
        var registryRow = await verifyConnection.QuerySingleAsync<RegistryProjectionRow>(
            """
            SELECT current_position AS CurrentPosition,
                   applied_event_version AS AppliedEventVersion,
                   last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId
            FROM sekiban_mv_registry
            WHERE view_name = 'WeatherForecast' AND logical_table = 'forecasts';
            """);

        Assert.Equal(updateEvent.SortableUniqueIdValue, registryRow.CurrentPosition);
        Assert.Equal(2, registryRow.AppliedEventVersion);
        Assert.Equal(updateEvent.SortableUniqueIdValue, registryRow.LastStreamAppliedSortableUniqueId);
    }

    [Fact]
    public async Task Grain_Late_Create_Older_Than_CurrentPosition_Is_Applied_Without_Stalling_Other_Aggregates()
    {
        if (!fixture.IsAvailable)
        {
            fixture.EnsureAvailable();
            return;
        }

        var grainKey = MvGrainKey.Build(DefaultServiceIdProvider.DefaultServiceId, "WeatherForecast", 1);
        var grain = fixture.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        try
        {
            await grain.RequestDeactivationAsync();
            await Task.Delay(200);
        }
        catch
        {
            // The grain may not be active yet; that's fine for this reset path.
        }

        await fixture.ResetAsync();
        await grain.EnsureStartedAsync();

        var executor = fixture.CreateExecutor(publishToStream: false);
        var delayedForecastId = Guid.CreateVersion7();
        var advancedForecastId = Guid.CreateVersion7();

        await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = delayedForecastId,
            Location = "Loc-late",
            Date = new DateOnly(2026, 4, 16),
            TemperatureC = 11,
            Summary = "Late create"
        });
        await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = advancedForecastId,
            Location = "Loc-advance",
            Date = new DateOnly(2026, 4, 17),
            TemperatureC = 12,
            Summary = "Advance position"
        });
        await executor.ExecuteAsync(new ChangeLocationName
        {
            ForecastId = delayedForecastId,
            NewLocationName = "Loc-late-U"
        });

        var allEvents = (await fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue()
            .Select(serializableEvent => new
            {
                SerializableEvent = serializableEvent,
                Event = serializableEvent.ToEvent(fixture.DomainTypes.EventTypes).GetValue()
            })
            .ToList();

        var delayedCreate = allEvents
            .Single(item => item.Event.Payload is WeatherForecastCreated created && created.ForecastId == delayedForecastId)
            .SerializableEvent;
        var delayedUpdate = allEvents
            .Single(item => item.Event.Payload is LocationNameChanged changed && changed.ForecastId == delayedForecastId)
            .SerializableEvent;
        var advancedCreate = allEvents
            .Single(item => item.Event.Payload is WeatherForecastCreated created && created.ForecastId == advancedForecastId)
            .SerializableEvent;

        Assert.True(
            string.Compare(delayedCreate.SortableUniqueIdValue, advancedCreate.SortableUniqueIdValue, StringComparison.Ordinal) < 0);
        Assert.True(
            string.Compare(advancedCreate.SortableUniqueIdValue, delayedUpdate.SortableUniqueIdValue, StringComparison.Ordinal) < 0);

        var streamNamespace = ServiceIdGrainKey.BuildStreamNamespace("AllEvents", DefaultServiceIdProvider.DefaultServiceId);
        var stream = fixture.Client
            .GetStreamProvider("EventStreamProvider")
            .GetStream<SerializableEvent>(StreamId.Create(streamNamespace, Guid.Empty));

        await stream.OnNextAsync(advancedCreate);
        await stream.OnNextAsync(delayedUpdate);
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        var blockedStatus = await grain.GetStatusAsync();
        Assert.Equal(advancedCreate.SortableUniqueIdValue, blockedStatus.CurrentPosition);

        await stream.OnNextAsync(delayedCreate);

        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            if (status.CurrentPosition != delayedUpdate.SortableUniqueIdValue)
            {
                return false;
            }

            await using var connection = await fixture.OpenConnectionAsync();
            var delayedRow = await connection.QuerySingleOrDefaultAsync<WeatherProjectionRow>(
                """
                SELECT forecast_id AS ForecastId,
                       location AS Location,
                       _last_sortable_unique_id AS LastSortableUniqueId
                FROM sekiban_mv_weatherforecast_v1_forecasts
                WHERE forecast_id = @ForecastId;
                """,
                new { ForecastId = delayedForecastId });
            var advancedRow = await connection.QuerySingleOrDefaultAsync<WeatherProjectionRow>(
                """
                SELECT forecast_id AS ForecastId,
                       location AS Location,
                       _last_sortable_unique_id AS LastSortableUniqueId
                FROM sekiban_mv_weatherforecast_v1_forecasts
                WHERE forecast_id = @ForecastId;
                """,
                new { ForecastId = advancedForecastId });

            return delayedRow is not null &&
                   delayedRow.Location == "Loc-late-U" &&
                   delayedRow.LastSortableUniqueId == delayedUpdate.SortableUniqueIdValue &&
                   advancedRow is not null &&
                   advancedRow.LastSortableUniqueId == advancedCreate.SortableUniqueIdValue;
        }, timeoutMs: 15000);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, int timeoutMs = 10000, int pollMs = 100)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(pollMs);
        }

        Assert.Fail("Condition was not satisfied before timeout.");
    }

    private sealed class OrderProjectionRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public string LastSortableUniqueId { get; set; } = string.Empty;
    }

    private sealed class RegistryProjectionRow
    {
        public string? CurrentPosition { get; set; }
        public string? LastSortableUniqueId { get; set; }
        public long AppliedEventVersion { get; set; }
        public string? LastAppliedSource { get; set; }
        public DateTimeOffset? LastAppliedAt { get; set; }
        public string? LastStreamReceivedSortableUniqueId { get; set; }
        public DateTimeOffset? LastStreamReceivedAt { get; set; }
        public string? LastStreamAppliedSortableUniqueId { get; set; }
        public string? LastCatchUpSortableUniqueId { get; set; }
    }

    private sealed class WeatherProjectionRow
    {
        public Guid ForecastId { get; set; }
        public string Location { get; set; } = string.Empty;
        public string LastSortableUniqueId { get; set; } = string.Empty;
    }
}
