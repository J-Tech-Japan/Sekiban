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
    [SkippableFact]
    public async Task Grain_CatchesUp_Then_Applies_Streamed_Event_To_Postgres_View()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres Orleans fixture is unavailable.");

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

        // Drive initial catch-up to idle so subsequent direct writes do not
        // race with the background catch-up tick. Startup itself is now
        // non-blocking; RefreshAsync() explicitly waits for catch-up settle.
        await grain.RefreshAsync();

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
        Assert.Equal("catchup", registryRow.LastAppliedSource);
        Assert.NotNull(registryRow.LastAppliedAt);
        Assert.Equal(latestAfterStream, registryRow.LastStreamReceivedSortableUniqueId);
        Assert.NotNull(registryRow.LastStreamReceivedAt);
        Assert.Null(registryRow.LastStreamAppliedSortableUniqueId);
        Assert.Equal(latestAfterStream, registryRow.LastCatchUpSortableUniqueId);
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

    [SkippableFact]
    public async Task Grain_RealPostgres_AC5_ReceiptRestartAndIdleRecovery_AreExactlyOnce()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres Orleans fixture is unavailable.");

        var grainKey = MvGrainKey.Build(DefaultServiceIdProvider.DefaultServiceId, "OrderSummary", 1);
        var grain = fixture.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        await DeactivateAndAwaitAsync(grain);
        await fixture.ResetAsync();
        await grain.RefreshAsync();

        var orderId = Guid.CreateVersion7();
        var firstItemId = Guid.CreateVersion7();
        var secondItemId = Guid.CreateVersion7();
        var durableExecutor = fixture.CreateExecutor(publishToStream: false);
        var streamNamespace = ServiceIdGrainKey.BuildStreamNamespace("AllEvents", DefaultServiceIdProvider.DefaultServiceId);
        var stream = fixture.Client
            .GetStreamProvider("EventStreamProvider")
            .GetStream<SerializableEvent>(StreamId.Create(streamNamespace, Guid.Empty));

        var catchUpGateReleased = 0;
        SerializableEvent? receiptEvent = null;
        var receiptSortableUniqueId = string.Empty;
        var receiptObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDeactivation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deactivationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (MaterializedViewGrain.PushBeforeCatchUpTestGate(_ => Volatile.Read(ref catchUpGateReleased) == 0))
        {
            try
            {
                await durableExecutor.ExecuteAsync(new CreateOrder
                {
                    OrderId = orderId,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
                });
                await durableExecutor.ExecuteAsync(new AddOrderItem
                {
                    OrderId = orderId,
                    ItemId = firstItemId,
                    ProductName = "Receipt barrier",
                    Quantity = 1,
                    UnitPrice = 15m,
                    AddedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                });

                var durableEvents = (await fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue()
                    .OrderBy(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
                    .ToList();
                receiptEvent = durableEvents.Single(serializableEvent =>
                    serializableEvent.ToEvent(fixture.DomainTypes.EventTypes).GetValue().Payload is OrderItemAdded added &&
                    added.ItemId == firstItemId);
                receiptSortableUniqueId = receiptEvent.SortableUniqueIdValue;

                using (MaterializedViewGrain.PushAfterStreamReceiptTestHookAsync(async candidate =>
                       {
                           receiptObserved.TrySetResult();
                           await releaseDeactivation.Task.WaitAsync(TimeSpan.FromSeconds(10));
                           await candidate.RequestDeactivationAsync();
                           return true;
                       }))
                using (MaterializedViewGrain.PushDeactivationTestHook(_ => deactivationObserved.TrySetResult()))
                {
                    Task? publish = null;
                    try
                    {
                        publish = stream.OnNextAsync(receiptEvent!);
                        await receiptObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));

                        var beforeApply = await ReadOrderStateAsync();
                        Assert.Null(beforeApply.Order);
                        Assert.Equal(0, beforeApply.ItemCount);
                        Assert.Equal(receiptSortableUniqueId, beforeApply.Registry.LastStreamReceivedSortableUniqueId);
                        Assert.NotNull(beforeApply.Registry.LastStreamReceivedAt);
                        Assert.Null(beforeApply.Registry.LastStreamAppliedSortableUniqueId);
                        Assert.Null(beforeApply.Registry.LastCatchUpSortableUniqueId);

                        releaseDeactivation.TrySetResult();
                        await publish;
                        await deactivationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    finally
                    {
                        // An assertion before the release must not strand the async stream callback or leave a
                        // catch-up gate closed for fixture teardown and the next test.
                        releaseDeactivation.TrySetResult();
                        if (publish is not null)
                        {
                            try
                            {
                                await publish.WaitAsync(TimeSpan.FromSeconds(10));
                                await deactivationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
                            }
                            catch
                            {
                                // Preserve the original assertion/failure; cleanup is best effort and bounded.
                            }
                        }
                    }
                }
            }
            finally
            {
                Volatile.Write(ref catchUpGateReleased, 1);
            }
        }

        var restarted = fixture.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        await restarted.EnsureStartedAsync();
        // OrderSummaryMvV1 applies OrderItemAdded additively. The registry AppliedEventVersion is the durable
        // application counter, so version 2 together with total 15/items 1 makes a replay observable.
        await WaitUntilAsync(async () =>
        {
            var state = await ReadOrderStateAsync();
            var status = await restarted.GetStatusAsync();
            return status.CurrentPosition == receiptSortableUniqueId &&
                   !status.CatchUpInProgress &&
                   !status.IsCatchUpActive &&
                   state.Order?.Total == 15m &&
                   state.ItemCount == 1 &&
                   state.Registry.CurrentPosition == receiptSortableUniqueId &&
                   state.Registry.LastAppliedSource == "catchup";
        }, timeoutMs: 15000);

        var afterReceiptRestart = await ReadOrderStateAsync();
        Assert.NotNull(afterReceiptRestart.Order);
        Assert.Equal(15m, afterReceiptRestart.Order!.Total);
        Assert.Equal(1, afterReceiptRestart.ItemCount);
        Assert.Equal(receiptSortableUniqueId, afterReceiptRestart.Registry.CurrentPosition);
        Assert.Equal(receiptSortableUniqueId, afterReceiptRestart.Registry.LastCatchUpSortableUniqueId);
        Assert.Equal(receiptSortableUniqueId, afterReceiptRestart.Registry.LastStreamReceivedSortableUniqueId);
        Assert.Equal(2, afterReceiptRestart.Registry.AppliedEventVersion);
        Assert.Null(afterReceiptRestart.Registry.LastStreamAppliedSortableUniqueId);

        await DeactivateAndAwaitAsync(restarted);
        var committedRestart = fixture.Client.GetGrain<IMaterializedViewGrain>(grainKey);
        await committedRestart.EnsureStartedAsync();
        await WaitUntilAsync(async () =>
        {
            var state = await ReadOrderStateAsync();
            var status = await committedRestart.GetStatusAsync();
            return status.CurrentPosition == receiptSortableUniqueId &&
                   !status.CatchUpInProgress &&
                   !status.IsCatchUpActive &&
                   state.Order?.Total == 15m &&
                   state.ItemCount == 1;
        }, timeoutMs: 15000);

        var beforeDuplicate = await ReadOrderStateAsync();
        Assert.Equal(2, beforeDuplicate.Registry.AppliedEventVersion);
        var receiptAtBeforeDuplicate = beforeDuplicate.Registry.LastStreamReceivedAt
            ?? throw new InvalidOperationException("The committed receipt did not expose a stream receipt timestamp.");

        await stream.OnNextAsync(receiptEvent!);
        await WaitUntilAsync(async () =>
        {
            var state = await ReadOrderStateAsync();
            return state.Registry.LastStreamReceivedAt is { } receivedAt &&
                   receivedAt > receiptAtBeforeDuplicate;
        }, timeoutMs: 15000);

        var duplicateReceiptState = await ReadOrderStateAsync();
        var duplicateReceiptAt = duplicateReceiptState.Registry.LastStreamReceivedAt
            ?? throw new InvalidOperationException("The duplicate receipt was not persisted.");
        // This is an application-clock baseline captured only after PostgreSQL positively observed the duplicate
        // receipt. The next strictly newer attempt is compared only with this same application status clock.
        var postReceiptCatchUpAttemptAt = (await committedRestart.GetStatusAsync()).LastCatchUpAttemptAt;

        await WaitUntilAsync(async () =>
        {
            var status = await committedRestart.GetStatusAsync();
            if (status.LastCatchUpAttemptAt is not { } catchUpAttemptAt ||
                (postReceiptCatchUpAttemptAt is { } priorAttempt && catchUpAttemptAt <= priorAttempt) ||
                status.CatchUpInProgress ||
                status.IsCatchUpActive ||
                status.CatchUpHalted ||
                status.BufferedEventCount != 0)
            {
                return false;
            }

            var state = await ReadOrderStateAsync();
            return state.Registry.LastStreamReceivedAt is { } receivedAt &&
                   receivedAt >= duplicateReceiptAt &&
                   state.Order?.Total == beforeDuplicate.Order?.Total &&
                   state.ItemCount == beforeDuplicate.ItemCount &&
                   state.Registry.CurrentPosition == beforeDuplicate.Registry.CurrentPosition;
        }, timeoutMs: 15000);

        var afterDuplicateStatus = await committedRestart.GetStatusAsync();
        Assert.True(
            afterDuplicateStatus.LastCatchUpAttemptAt is { } afterDuplicateAttemptAt &&
            (postReceiptCatchUpAttemptAt is not { } priorAttempt || afterDuplicateAttemptAt > priorAttempt));
        Assert.False(afterDuplicateStatus.CatchUpInProgress);
        Assert.False(afterDuplicateStatus.IsCatchUpActive);
        Assert.False(afterDuplicateStatus.CatchUpHalted);
        Assert.Equal(0, afterDuplicateStatus.BufferedEventCount);

        var afterDuplicate = await ReadOrderStateAsync();
        Assert.Equal(beforeDuplicate.Registry.CurrentPosition, afterDuplicate.Registry.CurrentPosition);
        Assert.Equal(beforeDuplicate.Registry.LastCatchUpSortableUniqueId, afterDuplicate.Registry.LastCatchUpSortableUniqueId);
        Assert.Equal(2, afterDuplicate.Registry.AppliedEventVersion);
        Assert.Equal(15m, afterDuplicate.Order?.Total);
        Assert.Equal(1, afterDuplicate.ItemCount);
        Assert.True(
            afterDuplicate.Registry.LastStreamReceivedAt is { } afterDuplicateReceiptAt &&
            afterDuplicateReceiptAt >= duplicateReceiptAt);
        Assert.Equal(beforeDuplicate.Registry.LastStreamReceivedSortableUniqueId, afterDuplicate.Registry.LastStreamReceivedSortableUniqueId);
        Assert.Null(afterDuplicate.Registry.LastStreamAppliedSortableUniqueId);

        var beforeIdle = afterDuplicate;
        var idleReceiptAt = beforeIdle.Registry.LastStreamReceivedAt;
        await durableExecutor.ExecuteAsync(new AddOrderItem
        {
            OrderId = orderId,
            ItemId = secondItemId,
            ProductName = "Idle recovery",
            Quantity = 1,
            UnitPrice = 5m,
            AddedAt = DateTimeOffset.UtcNow
        });
        var idleSortableUniqueId = (await fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue()
            .OrderByDescending(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
            .First()
            .SortableUniqueIdValue;

        await WaitUntilAsync(async () =>
        {
            var state = await ReadOrderStateAsync();
            var status = await committedRestart.GetStatusAsync();
            return status.CurrentPosition == idleSortableUniqueId &&
                   !status.CatchUpInProgress &&
                   !status.IsCatchUpActive &&
                   state.Order?.Total == 20m &&
                   state.ItemCount == 2 &&
                   state.Registry.CurrentPosition == idleSortableUniqueId &&
                   state.Registry.LastCatchUpSortableUniqueId == idleSortableUniqueId &&
                   state.Registry.LastStreamReceivedSortableUniqueId == receiptSortableUniqueId &&
                   state.Registry.LastStreamReceivedAt == idleReceiptAt;
        }, timeoutMs: 15000);

        var afterIdle = await ReadOrderStateAsync();
        Assert.NotNull(afterIdle.Order);
        Assert.Equal(20m, afterIdle.Order!.Total);
        Assert.Equal(2, afterIdle.ItemCount);
        Assert.Equal(idleSortableUniqueId, afterIdle.Registry.CurrentPosition);
        Assert.Equal(idleSortableUniqueId, afterIdle.Registry.LastCatchUpSortableUniqueId);
        Assert.Equal(3, afterIdle.Registry.AppliedEventVersion);
        Assert.Equal(receiptSortableUniqueId, afterIdle.Registry.LastStreamReceivedSortableUniqueId);
        Assert.Equal(idleReceiptAt, afterIdle.Registry.LastStreamReceivedAt);
        Assert.Null(afterIdle.Registry.LastStreamAppliedSortableUniqueId);

        async Task DeactivateAndAwaitAsync(IMaterializedViewGrain target)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (MaterializedViewGrain.PushDeactivationTestHook(_ => completed.TrySetResult()))
            {
                await target.RequestDeactivationAsync();
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        async Task<(OrderProjectionRow? Order, int ItemCount, RegistryProjectionRow Registry)> ReadOrderStateAsync()
        {
            await using var connection = await fixture.OpenConnectionAsync();
            var order = await connection.QuerySingleOrDefaultAsync<OrderProjectionRow>(
                """
                SELECT id,
                       status,
                       total,
                       _last_sortable_unique_id AS LastSortableUniqueId
                FROM sekiban_mv_ordersummary_v1_orders
                WHERE id = @OrderId;
                """,
                new { OrderId = orderId });
            var itemCount = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sekiban_mv_ordersummary_v1_items WHERE order_id = @OrderId;",
                new { OrderId = orderId });
            var registry = await connection.QuerySingleAsync<RegistryProjectionRow>(
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
            return (order, itemCount, registry);
        }
    }

    [SkippableFact]
    public async Task Grain_Reactivation_RestoresMixedLegacyStatuses_WithoutCatchUpDowngrade()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres Orleans fixture is unavailable.");

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
        await grain.RefreshAsync();

        var before = await ReadLifecycleRowsAsync();
        var activeBefore = await ReadActivePointerAsync();
        Assert.Equal(2, before.Count);
        Assert.All(before, row => Assert.Equal("active", row.Status));
        Assert.Equal(1, activeBefore.ActiveVersion);

        await grain.RequestDeactivationAsync();
        await Task.Delay(200);

        await using (var damageConnection = await fixture.OpenConnectionAsync())
        {
            await damageConnection.ExecuteAsync(
                """
                UPDATE sekiban_mv_registry
                SET status = CASE logical_table
                    WHEN 'orders' THEN 'catchingup'
                    ELSE 'active'
                END
                WHERE service_id = @ServiceId
                  AND view_name = 'OrderSummary'
                  AND view_version = 1;
                """,
                new { ServiceId = DefaultServiceIdProvider.DefaultServiceId });
        }

        var damaged = await ReadLifecycleRowsAsync();
        Assert.Equal("catchingup", damaged.Single(row => row.LogicalTable == "orders").Status);
        Assert.Equal("active", damaged.Single(row => row.LogicalTable == "items").Status);

        // Reactivation starts the normal background lifecycle; no explicit RefreshAsync is used here.
        await grain.EnsureStartedAsync();
        await WaitUntilAsync(async () =>
        {
            var rows = await ReadLifecycleRowsAsync();
            var active = await ReadActivePointerAsync();
            return rows.Count == 2 &&
                   rows.All(row => row.Status == "active") &&
                   active.ActiveVersion == activeBefore.ActiveVersion &&
                   active.ActiveGeneration == activeBefore.ActiveGeneration;
        }, timeoutMs: 15000);

        var after = await ReadLifecycleRowsAsync();
        var status = await grain.GetStatusAsync();
        Assert.True(status.Started);
        Assert.All(after, row => Assert.Equal("active", row.Status));
        AssertReactivationLifecycleDataUnchanged(before, after);

        var activeAfter = await ReadActivePointerAsync();
        Assert.Equal(activeBefore, activeAfter);
    }

    [SkippableFact]
    public async Task Grain_ActiveRefresh_PreservesLifecycleAndPublishesIndependentProgress()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres Orleans fixture is unavailable.");

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
        await grain.RefreshAsync();
        var activeBefore = await ReadActivePointerAsync();

        var orderId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        var executor = fixture.CreateExecutor(publishToStream: false);
        await executor.ExecuteAsync(new CreateOrder
        {
            OrderId = orderId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await executor.ExecuteAsync(new AddOrderItem
        {
            OrderId = orderId,
            ItemId = itemId,
            ProductName = "G57 refresh",
            Quantity = 1,
            UnitPrice = 7m,
            AddedAt = DateTimeOffset.UtcNow
        });

        var latest = (await fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue()
            .OrderByDescending(serializableEvent => serializableEvent.SortableUniqueIdValue, StringComparer.Ordinal)
            .First()
            .SortableUniqueIdValue;

        await grain.RefreshAsync();
        var afterFirstRefresh = await ReadLifecycleRowsAsync();
        await grain.RefreshAsync();
        var afterRepeatedRefresh = await ReadLifecycleRowsAsync();

        var status = await grain.GetStatusAsync();
        Assert.Equal(latest, status.CurrentPosition);
        Assert.All(afterFirstRefresh, row => Assert.Equal("active", row.Status));
        Assert.All(afterRepeatedRefresh, row => Assert.Equal("active", row.Status));
        Assert.Equal(activeBefore, await ReadActivePointerAsync());

        var ordersFirst = afterFirstRefresh.Single(row => row.LogicalTable == "orders");
        var itemsFirst = afterFirstRefresh.Single(row => row.LogicalTable == "items");
        Assert.Equal(latest, ordersFirst.CurrentPosition);
        Assert.Equal(latest, itemsFirst.CurrentPosition);
        Assert.Equal(2, ordersFirst.AppliedEventVersion);
        Assert.Equal(2, itemsFirst.AppliedEventVersion);
        Assert.Equal(latest, ordersFirst.LastCatchUpSortableUniqueId);
        Assert.Equal(latest, itemsFirst.LastCatchUpSortableUniqueId);
        Assert.Equal(afterFirstRefresh, afterRepeatedRefresh);
    }

    [SkippableFact]
    public async Task Grain_OutOfOrder_StreamDelivery_DoesNotLose_WeatherForecastRows()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres Orleans fixture is unavailable.");

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
        // Drive initial catch-up to idle so subsequent direct writes do not
        // race with the background catch-up tick. Startup itself is now
        // non-blocking; RefreshAsync() explicitly waits for catch-up settle.
        await grain.RefreshAsync();

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
        Assert.Equal("catchup", registryRow.LastAppliedSource);
        Assert.NotNull(registryRow.LastAppliedAt);
        Assert.Equal(latestSortableUniqueId, registryRow.LastStreamReceivedSortableUniqueId);
        Assert.NotNull(registryRow.LastStreamReceivedAt);
        Assert.Null(registryRow.LastStreamAppliedSortableUniqueId);
        Assert.Equal(latestSortableUniqueId, registryRow.LastCatchUpSortableUniqueId);
    }

    [SkippableFact]
    public async Task Grain_DurableCatchUp_UsesStoreOrder_When_UpdateHintArrivesFirst()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres Orleans fixture is unavailable.");

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
        // Drive initial catch-up to idle so subsequent direct writes do not
        // race with the background catch-up tick. Startup itself is now
        // non-blocking; RefreshAsync() explicitly waits for catch-up settle.
        await grain.RefreshAsync();

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

        // The predecessor notification may arrive after durable catch-up already applied the ordered pair. It is a
        // duplicate-compatible receipt and must not move the durable checkpoint backward or invoke stream DML.
        await stream.OnNextAsync(createEvent);

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
        Assert.Null(registryRow.LastStreamAppliedSortableUniqueId);
        Assert.Equal(updateEvent.SortableUniqueIdValue, registryRow.LastCatchUpSortableUniqueId);
    }

    [SkippableFact]
    public async Task Grain_Streamed_Create_Then_Update_For_Same_Aggregate_Applies_Both_Events()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres Orleans fixture is unavailable.");

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
        // Drive initial catch-up to idle so subsequent direct writes do not
        // race with the background catch-up tick. Startup itself is now
        // non-blocking; RefreshAsync() explicitly waits for catch-up settle.
        await grain.RefreshAsync();

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
                   last_applied_source AS LastAppliedSource,
                   last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId,
                   last_catch_up_sortable_unique_id AS LastCatchUpSortableUniqueId
            FROM sekiban_mv_registry
            WHERE view_name = 'WeatherForecast' AND logical_table = 'forecasts';
            """);

        Assert.Equal(updateEvent.SortableUniqueIdValue, registryRow.CurrentPosition);
        Assert.Equal(2, registryRow.AppliedEventVersion);
        Assert.Equal("catchup", registryRow.LastAppliedSource);
        Assert.Null(registryRow.LastStreamAppliedSortableUniqueId);
        Assert.Equal(updateEvent.SortableUniqueIdValue, registryRow.LastCatchUpSortableUniqueId);
    }

    [SkippableFact]
    public async Task Grain_Late_Create_Older_Than_CurrentPosition_Is_Applied_Without_Stalling_Other_Aggregates()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Postgres Orleans fixture is unavailable.");

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
        // Drive initial catch-up to idle so subsequent direct writes do not
        // race with the background catch-up tick. Startup itself is now
        // non-blocking; RefreshAsync() explicitly waits for catch-up settle.
        await grain.RefreshAsync();

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

        // All three events are already durable. The first two notifications must therefore allow the
        // ordered store catch-up to finish both rows before the older predecessor receipt arrives.
        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            if (status.CurrentPosition != delayedUpdate.SortableUniqueIdValue ||
                status.CatchUpInProgress ||
                status.IsCatchUpActive ||
                status.CatchUpHalted ||
                status.BufferedEventCount != 0 ||
                status.LastCatchUpAttemptAt is null)
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
            var receipt = await connection.QuerySingleAsync<RegistryProjectionRow>(
                """
                SELECT last_stream_received_sortable_unique_id AS LastStreamReceivedSortableUniqueId,
                       last_stream_received_at AS LastStreamReceivedAt
                FROM sekiban_mv_registry
                WHERE view_name = 'WeatherForecast' AND logical_table = 'forecasts';
                """);

            return delayedRow is not null &&
                   delayedRow.Location == "Loc-late-U" &&
                   delayedRow.LastSortableUniqueId == delayedUpdate.SortableUniqueIdValue &&
                   advancedRow is not null &&
                   advancedRow.LastSortableUniqueId == advancedCreate.SortableUniqueIdValue &&
                   receipt.LastStreamReceivedAt is not null &&
                   receipt.LastStreamReceivedSortableUniqueId == delayedUpdate.SortableUniqueIdValue;
        }, timeoutMs: 15000);

        async Task<(RegistryProjectionRow Registry, WeatherProjectionRow? DelayedRow, WeatherProjectionRow? AdvancedRow)> ReadStateAsync()
        {
            await using var connection = await fixture.OpenConnectionAsync();
            var registry = await connection.QuerySingleAsync<RegistryProjectionRow>(
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
            return (registry, delayedRow, advancedRow);
        }

        var baselineStatus = await grain.GetStatusAsync();
        Assert.NotNull(baselineStatus.LastCatchUpAttemptAt);
        Assert.False(baselineStatus.CatchUpInProgress);
        Assert.False(baselineStatus.IsCatchUpActive);
        Assert.False(baselineStatus.CatchUpHalted);
        Assert.Equal(0, baselineStatus.BufferedEventCount);

        var beforeDuplicate = await ReadStateAsync();
        Assert.Equal(delayedUpdate.SortableUniqueIdValue, beforeDuplicate.Registry.CurrentPosition);
        Assert.Equal(3, beforeDuplicate.Registry.AppliedEventVersion);
        Assert.Equal("catchup", beforeDuplicate.Registry.LastAppliedSource);
        // The initial durable catch-up is allowed to have no stream receipt. The wait above separately proves that
        // both sent hints reached the durable receipt marker before this duplicate baseline is captured.
        var baselineStreamReceivedAt = beforeDuplicate.Registry.LastStreamReceivedAt
            ?? throw new InvalidOperationException("The two initial stream hints did not record a receipt timestamp.");
        Assert.Equal(delayedUpdate.SortableUniqueIdValue, beforeDuplicate.Registry.LastStreamReceivedSortableUniqueId);
        Assert.Null(beforeDuplicate.Registry.LastStreamAppliedSortableUniqueId);
        Assert.Equal(delayedUpdate.SortableUniqueIdValue, beforeDuplicate.Registry.LastCatchUpSortableUniqueId);
        Assert.NotNull(beforeDuplicate.DelayedRow);
        Assert.Equal("Loc-late-U", beforeDuplicate.DelayedRow.Location);
        Assert.Equal(delayedUpdate.SortableUniqueIdValue, beforeDuplicate.DelayedRow.LastSortableUniqueId);
        Assert.NotNull(beforeDuplicate.AdvancedRow);
        Assert.Equal(advancedCreate.SortableUniqueIdValue, beforeDuplicate.AdvancedRow.LastSortableUniqueId);

        // The predecessor notification is an older duplicate receipt: it must be observable as a receipt without
        // regressing the durable checkpoint, invoking stream DML, or reapplying any event.
        await stream.OnNextAsync(delayedCreate);

        // An idle probe can also advance LastCatchUpAttemptAt. First observe the duplicate's receipt through the
        // provider-owned timestamp that MarkStreamReceivedAsync updates even when the SUID is older and therefore
        // cannot replace LastStreamReceivedSortableUniqueId. Only after that causal receipt observation do we wait for
        // a completed catch-up attempt.
        await WaitUntilAsync(async () =>
        {
            var state = await ReadStateAsync();
            return state.Registry.LastStreamReceivedAt is { } receivedAt &&
                   receivedAt > baselineStreamReceivedAt;
        }, timeoutMs: 15000);

        var duplicateReceiptState = await ReadStateAsync();
        var duplicateReceiptAt = duplicateReceiptState.Registry.LastStreamReceivedAt
            ?? throw new InvalidOperationException("The older duplicate receipt was not persisted.");
        var postReceiptCatchUpAttemptAt = (await grain.GetStatusAsync()).LastCatchUpAttemptAt
            ?? throw new InvalidOperationException("The post-receipt state did not expose a catch-up attempt marker.");

        await WaitUntilAsync(async () =>
        {
            var status = await grain.GetStatusAsync();
            if (status.LastCatchUpAttemptAt is not { } catchUpAttemptAt ||
                catchUpAttemptAt <= postReceiptCatchUpAttemptAt ||
                status.CatchUpInProgress ||
                status.IsCatchUpActive ||
                status.CatchUpHalted ||
                status.BufferedEventCount != 0)
            {
                return false;
            }

            var state = await ReadStateAsync();
            return state.Registry.LastStreamReceivedAt is { } receivedAt &&
                   receivedAt >= duplicateReceiptAt &&
                   state.Registry.CurrentPosition == beforeDuplicate.Registry.CurrentPosition &&
                   state.Registry.AppliedEventVersion == beforeDuplicate.Registry.AppliedEventVersion &&
                   state.Registry.LastStreamReceivedSortableUniqueId == beforeDuplicate.Registry.LastStreamReceivedSortableUniqueId &&
                   state.Registry.LastStreamAppliedSortableUniqueId is null &&
                   state.Registry.LastCatchUpSortableUniqueId == beforeDuplicate.Registry.LastCatchUpSortableUniqueId &&
                   state.DelayedRow is not null &&
                   state.DelayedRow.Location == beforeDuplicate.DelayedRow!.Location &&
                   state.DelayedRow.LastSortableUniqueId == beforeDuplicate.DelayedRow.LastSortableUniqueId &&
                   state.AdvancedRow is not null &&
                   state.AdvancedRow.LastSortableUniqueId == beforeDuplicate.AdvancedRow!.LastSortableUniqueId;
        }, timeoutMs: 15000);

        var afterDuplicateStatus = await grain.GetStatusAsync();
        Assert.True(
            afterDuplicateStatus.LastCatchUpAttemptAt is { } afterCatchUpAttemptAt &&
            afterCatchUpAttemptAt > postReceiptCatchUpAttemptAt);
        Assert.False(afterDuplicateStatus.CatchUpInProgress);
        Assert.False(afterDuplicateStatus.IsCatchUpActive);
        Assert.False(afterDuplicateStatus.CatchUpHalted);
        Assert.Equal(0, afterDuplicateStatus.BufferedEventCount);

        var afterDuplicate = await ReadStateAsync();
        Assert.Equal(beforeDuplicate.Registry.CurrentPosition, afterDuplicate.Registry.CurrentPosition);
        Assert.Equal(beforeDuplicate.Registry.AppliedEventVersion, afterDuplicate.Registry.AppliedEventVersion);
        Assert.Equal("catchup", afterDuplicate.Registry.LastAppliedSource);
        Assert.True(
            afterDuplicate.Registry.LastStreamReceivedAt is { } afterReceiptAt &&
            afterReceiptAt >= duplicateReceiptAt,
            "The older duplicate receipt must remain observable through LastStreamReceivedAt.");
        Assert.Equal(beforeDuplicate.Registry.LastStreamReceivedSortableUniqueId, afterDuplicate.Registry.LastStreamReceivedSortableUniqueId);
        Assert.Null(afterDuplicate.Registry.LastStreamAppliedSortableUniqueId);
        Assert.Equal(beforeDuplicate.Registry.LastCatchUpSortableUniqueId, afterDuplicate.Registry.LastCatchUpSortableUniqueId);
        Assert.NotNull(afterDuplicate.DelayedRow);
        Assert.Equal(beforeDuplicate.DelayedRow!.Location, afterDuplicate.DelayedRow.Location);
        Assert.Equal(beforeDuplicate.DelayedRow.LastSortableUniqueId, afterDuplicate.DelayedRow.LastSortableUniqueId);
        Assert.NotNull(afterDuplicate.AdvancedRow);
        Assert.Equal(beforeDuplicate.AdvancedRow!.LastSortableUniqueId, afterDuplicate.AdvancedRow.LastSortableUniqueId);
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

    private async Task<IReadOnlyList<LifecycleRegistryRow>> ReadLifecycleRowsAsync()
    {
        await using var connection = await fixture.OpenConnectionAsync();
        var rows = await connection.QueryAsync<LifecycleRegistryRow>(
            """
            SELECT logical_table AS LogicalTable,
                   physical_table AS PhysicalTable,
                   status AS Status,
                   current_position AS CurrentPosition,
                   target_position AS TargetPosition,
                   current_checkpoint_truth::text AS CurrentCheckpointTruth,
                   target_checkpoint_truth::text AS TargetCheckpointTruth,
                   last_sortable_unique_id AS LastSortableUniqueId,
                   applied_event_version AS AppliedEventVersion,
                   last_applied_source AS LastAppliedSource,
                   last_applied_at AS LastAppliedAt,
                   last_stream_received_sortable_unique_id AS LastStreamReceivedSortableUniqueId,
                   last_stream_received_at AS LastStreamReceivedAt,
                   last_stream_applied_sortable_unique_id AS LastStreamAppliedSortableUniqueId,
                   last_catch_up_sortable_unique_id AS LastCatchUpSortableUniqueId,
                   metadata::text AS Metadata
            FROM sekiban_mv_registry
            WHERE service_id = @ServiceId
              AND view_name = 'OrderSummary'
              AND view_version = 1
            ORDER BY logical_table;
            """,
            new { ServiceId = DefaultServiceIdProvider.DefaultServiceId });
        return rows.ToList();
    }

    private async Task<ActivePointerRow> ReadActivePointerAsync()
    {
        await using var connection = await fixture.OpenConnectionAsync();
        return await connection.QuerySingleAsync<ActivePointerRow>(
            """
            SELECT active_version AS ActiveVersion,
                   active_generation AS ActiveGeneration,
                   activated_at AS ActivatedAt,
                   switch_kind AS SwitchKind,
                   switch_reason AS SwitchReason,
                   switched_at_utc AS SwitchedAtUtc
            FROM sekiban_mv_active
            WHERE service_id = @ServiceId
              AND view_name = 'OrderSummary';
            """,
            new { ServiceId = DefaultServiceIdProvider.DefaultServiceId });
    }

    private static void AssertReactivationLifecycleDataUnchanged(
        IReadOnlyList<LifecycleRegistryRow> before,
        IReadOnlyList<LifecycleRegistryRow> after)
    {
        Assert.Equal(before.Count, after.Count);
        foreach (var expected in before)
        {
            var actual = after.Single(row => row.LogicalTable == expected.LogicalTable);
            Assert.Equal(expected.LogicalTable, actual.LogicalTable);
            Assert.Equal(expected.PhysicalTable, actual.PhysicalTable);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.CurrentPosition, actual.CurrentPosition);
            Assert.Equal(expected.TargetPosition, actual.TargetPosition);
            Assert.Equal(expected.CurrentCheckpointTruth, actual.CurrentCheckpointTruth);
            AssertTargetCheckpointTruthUnchangedExceptStartupCaptureTimestamp(
                expected.TargetCheckpointTruth,
                actual.TargetCheckpointTruth);
            Assert.Equal(expected.LastSortableUniqueId, actual.LastSortableUniqueId);
            Assert.Equal(expected.AppliedEventVersion, actual.AppliedEventVersion);
            Assert.Equal(expected.LastAppliedSource, actual.LastAppliedSource);
            Assert.Equal(expected.LastAppliedAt, actual.LastAppliedAt);
            Assert.Equal(expected.LastStreamReceivedSortableUniqueId, actual.LastStreamReceivedSortableUniqueId);
            Assert.Equal(expected.LastStreamReceivedAt, actual.LastStreamReceivedAt);
            Assert.Equal(expected.LastStreamAppliedSortableUniqueId, actual.LastStreamAppliedSortableUniqueId);
            Assert.Equal(expected.LastCatchUpSortableUniqueId, actual.LastCatchUpSortableUniqueId);
            Assert.Equal(expected.Metadata, actual.Metadata);
        }
    }

    private static void AssertTargetCheckpointTruthUnchangedExceptStartupCaptureTimestamp(
        string? expectedSerialized,
        string? actualSerialized)
    {
        var expected = MvCheckpointTruthCodec.Decode(expectedSerialized);
        var actual = MvCheckpointTruthCodec.Decode(actualSerialized);

        Assert.Equal(expected.State, actual.State);
        Assert.Equal(expected.IsKnownZero, actual.IsKnownZero);
        Assert.Equal(expected.PositionValue, actual.PositionValue);
        Assert.Equal(expected.UnknownReason, actual.UnknownReason);

        Assert.NotNull(expected.Provenance);
        Assert.NotNull(actual.Provenance);
        var expectedProvenance = expected.Provenance!;
        var actualProvenance = actual.Provenance!;
        Assert.Equal(MvCheckpointProvenanceKind.AuthoritativeTargetCapture, expectedProvenance.Kind);
        Assert.Equal(expectedProvenance.Kind, actualProvenance.Kind);
        Assert.Equal(expectedProvenance.ApplySource, actualProvenance.ApplySource);

        Assert.True(
            actualProvenance.ObservedAtUtc >= expectedProvenance.ObservedAtUtc,
            $"Startup target capture provenance regressed from {expectedProvenance.ObservedAtUtc:O} to {actualProvenance.ObservedAtUtc:O}.");
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

    private sealed record LifecycleRegistryRow(
        string LogicalTable,
        string PhysicalTable,
        string Status,
        string? CurrentPosition,
        string? TargetPosition,
        string? CurrentCheckpointTruth,
        string? TargetCheckpointTruth,
        string? LastSortableUniqueId,
        long AppliedEventVersion,
        string? LastAppliedSource,
        DateTime? LastAppliedAt,
        string? LastStreamReceivedSortableUniqueId,
        DateTime? LastStreamReceivedAt,
        string? LastStreamAppliedSortableUniqueId,
        string? LastCatchUpSortableUniqueId,
        string? Metadata);

    private sealed record ActivePointerRow(
        int ActiveVersion,
        long ActiveGeneration,
        DateTime ActivatedAt,
        string SwitchKind,
        string? SwitchReason,
        DateTime? SwitchedAtUtc);

    private sealed class WeatherProjectionRow
    {
        public Guid ForecastId { get; set; }
        public string Location { get; set; } = string.Empty;
        public string LastSortableUniqueId { get; set; } = string.Empty;
    }
}
