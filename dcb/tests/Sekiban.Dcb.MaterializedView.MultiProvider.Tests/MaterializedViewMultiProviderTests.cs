using Dapper;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

/// <summary>
///     Uses an update-only statement for the update event. It is the provider-backed discriminator for the removed
///     inline stream-DML path: descending updates affect zero rows until their creates arrive, while durable catch-up
///     orders the same aged history and applies every pair.
/// </summary>
internal sealed class FixedAgedInlineLossProjector(int version) : IMaterializedViewProjector
{
    public string ViewName => "G57InlineLoss";
    public int ViewVersion => version;
    public MvTable Rows { get; private set; } = default!;

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        Rows = ctx.RegisterTable("rows");
        await ctx.ExecuteAsync(CreateTableSql(ctx.DatabaseType, Rows.PhysicalName), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await ctx.ExecuteAsync($"DELETE FROM {Rows.PhysicalName};", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default)
    {
        var parameters = ev.Payload switch
        {
            WeatherForecastCreated created => new
            {
                ForecastId = created.ForecastId.ToString("D"),
                Location = created.Location,
                SortableUniqueId = ctx.CurrentSortableUniqueId
            },
            WeatherForecastUpdated updated => new
            {
                ForecastId = updated.ForecastId.ToString("D"),
                Location = updated.Location,
                SortableUniqueId = ctx.CurrentSortableUniqueId
            },
            _ => null
        };

        if (parameters is null)
        {
            return Task.FromResult<IReadOnlyList<MvSqlStatement>>([]);
        }

        var sql = ev.Payload switch
        {
            WeatherForecastCreated => $"INSERT INTO {Rows.PhysicalName} (forecast_id, location, _last_sortable_unique_id) VALUES (@ForecastId, @Location, @SortableUniqueId);",
            WeatherForecastUpdated => $"UPDATE {Rows.PhysicalName} SET location = @Location, _last_sortable_unique_id = @SortableUniqueId WHERE forecast_id = @ForecastId AND _last_sortable_unique_id < @SortableUniqueId;",
            _ => string.Empty
        };
        return Task.FromResult<IReadOnlyList<MvSqlStatement>>([new MvSqlStatement(sql, parameters)]);
    }

    private static string CreateTableSql(MvDbType databaseType, string tableName) => databaseType switch
    {
        MvDbType.Postgres => $"CREATE TABLE IF NOT EXISTS {tableName} (forecast_id TEXT NOT NULL PRIMARY KEY, location TEXT NOT NULL, _last_sortable_unique_id TEXT NOT NULL);",
        MvDbType.MySql => $"CREATE TABLE IF NOT EXISTS {tableName} (forecast_id VARCHAR(64) NOT NULL PRIMARY KEY, location VARCHAR(256) NOT NULL, _last_sortable_unique_id VARCHAR(64) NOT NULL);",
        MvDbType.Sqlite => $"CREATE TABLE IF NOT EXISTS {tableName} (forecast_id TEXT NOT NULL PRIMARY KEY, location TEXT NOT NULL, _last_sortable_unique_id TEXT NOT NULL);",
        MvDbType.SqlServer => $"IF OBJECT_ID(N'{tableName}', N'U') IS NULL CREATE TABLE {tableName} (forecast_id NVARCHAR(64) NOT NULL PRIMARY KEY, location NVARCHAR(256) NOT NULL, _last_sortable_unique_id NVARCHAR(64) NOT NULL);",
        _ => throw new NotSupportedException($"Database type '{databaseType}' is not supported.")
    };
}

[CollectionDefinition(nameof(MySqlMvCollection))]
public sealed class MySqlMvCollection : ICollectionFixture<MySqlMvFixture>;

[CollectionDefinition(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvCollection : ICollectionFixture<SqlServerMvFixture>;

[CollectionDefinition(nameof(SqliteMvCollection))]
public sealed class SqliteMvCollection : ICollectionFixture<SqliteMvFixture>;

public sealed class MaterializedViewMultiProviderRegistrationTests
{
    [Fact]
    public void SqlServer_Registration_ReportsStorageInfo()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbMaterializedViewSqlServer("Server=(local);Database=test;User Id=sa;Password=Password123!;", registerHostedWorker: false);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo();
        Assert.Equal(MvDbType.SqlServer, storage.DatabaseType);
    }

    [Fact]
    public void MySql_Registration_ReportsStorageInfo()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbMaterializedViewMySql("Server=localhost;Database=test;User Id=root;Password=test;", registerHostedWorker: false);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo();
        Assert.Equal(MvDbType.MySql, storage.DatabaseType);
    }

    [Fact]
    public void Sqlite_Registration_ReportsStorageInfo()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbMaterializedViewSqlite("Data Source=:memory:", registerHostedWorker: false);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo();
        Assert.Equal(MvDbType.Sqlite, storage.DatabaseType);
    }
}

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvIntegrationTests(MySqlMvFixture fixture)
{
    [SkippableFact]
    public Task CatchUp_MaterializesRowsAndRegistry() => MultiProviderAssertions.AssertProviderWorksAsync(fixture);

    [SkippableFact]
    public Task LegacyRegistry_IsMigratedWithoutRowLoss() => MultiProviderAssertions.AssertLegacyRegistryMigratesAsync(fixture);

    [SkippableFact]
    public Task FixedAgedDescendingPairs_KillSequentialInlineLoss() =>
        MultiProviderAssertions.AssertFixedAgedDescendingPairsAsync(fixture);
}

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvIntegrationTests(SqlServerMvFixture fixture)
{
    [SkippableFact]
    public Task CatchUp_MaterializesRowsAndRegistry() => MultiProviderAssertions.AssertProviderWorksAsync(fixture);

    [SkippableFact]
    public Task LegacyRegistry_IsMigratedWithoutRowLoss() => MultiProviderAssertions.AssertLegacyRegistryMigratesAsync(fixture);

    [SkippableFact]
    public Task FixedAgedDescendingPairs_KillSequentialInlineLoss() =>
        MultiProviderAssertions.AssertFixedAgedDescendingPairsAsync(fixture);
}

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvIntegrationTests(SqliteMvFixture fixture)
{
    [SkippableFact]
    public Task CatchUp_MaterializesRowsAndRegistry() => MultiProviderAssertions.AssertProviderWorksAsync(fixture);

    [SkippableFact]
    public Task LegacyRegistry_IsMigratedWithoutRowLoss() => MultiProviderAssertions.AssertLegacyRegistryMigratesAsync(fixture);

    [SkippableFact]
    public Task SharedEventBackend_UsesOnlyTheRequestedService() =>
        MultiProviderAssertions.AssertServiceIsolationAsync(fixture);

    [SkippableFact]
    public Task FixedAgedDescendingPairs_KillSequentialInlineLoss() =>
        MultiProviderAssertions.AssertFixedAgedDescendingPairsAsync(fixture);
}

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvIntegrationTests(PostgresMvFixture fixture)
{
    [SkippableFact]
    public Task FixedAgedDescendingPairs_KillSequentialInlineLoss() =>
        MultiProviderAssertions.AssertFixedAgedDescendingPairsAsync(fixture);
}

internal static class MultiProviderAssertions
{
    public static async Task AssertLegacyRegistryMigratesAsync(MultiProviderFixtureBase fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");

        await fixture.PrepareLegacyRegistryAsync().ConfigureAwait(false);
        var registryStore = fixture.Services.GetRequiredService<IMvRegistryStore>();
        await registryStore.EnsureInfrastructureAsync().ConfigureAwait(false);

        var entries = await registryStore.GetEntriesAsync("legacy-service", "Legacy", 1).ConfigureAwait(false);
        var entry = Assert.Single(entries);
        Assert.Equal("legacy_orders", entry.PhysicalTable);
        Assert.True(entry.CurrentCheckpointTruth.IsUnknown);
        Assert.Equal(MvCheckpointUnknownReason.LegacyNull, entry.CurrentCheckpointTruth.UnknownReason);
        Assert.Null(entry.CurrentPosition);
        Assert.Equal(1, await CountLegacyRowsAsync(fixture).ConfigureAwait(false));
    }

    private static async Task<long> CountLegacyRowsAsync(MultiProviderFixtureBase fixture)
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sekiban_mv_registry WHERE service_id = 'legacy-service';")
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Uses the real provider executor for both the repaired durable store read and the removed one-event inline
    ///     mutant. The fixed-aged descending 64-pair history must materialize all updates through catch-up, while the
    ///     old inline/global-cursor path must lose at least one update.
    /// </summary>
    public static async Task AssertFixedAgedDescendingPairsAsync(MultiProviderFixtureBase fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");

        var projector = new FixedAgedInlineLossProjector(1);
        var host = new NativeMvApplyHost(
            projector,
            fixture.DomainTypes.EventTypes,
            fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo().DatabaseType);
        var durableEvents = CreateFixedAgedDescendingPairs(fixture);

        await fixture.ResetAsync().ConfigureAwait(false);
        await fixture.Executor.InitializeAsync(host).ConfigureAwait(false);
        var writeResult = await fixture.EventStore.WriteSerializableEventsAsync(durableEvents).ConfigureAwait(false);
        Assert.True(writeResult.IsSuccess, writeResult.IsSuccess ? string.Empty : writeResult.GetException().Message);

        var appliedByStore = 0;
        for (var attempt = 0; attempt < 4 && appliedByStore < durableEvents.Count; attempt++)
        {
            appliedByStore += (await fixture.Executor.CatchUpOnceAsync(host).ConfigureAwait(false)).AppliedEvents;
        }

        Assert.Equal(durableEvents.Count, appliedByStore);
        Assert.Equal(64, await CountUpdatedRowsAsync(fixture, projector.Rows.PhysicalName).ConfigureAwait(false));

        await fixture.ResetAsync().ConfigureAwait(false);
        await fixture.Executor.InitializeAsync(host).ConfigureAwait(false);
        writeResult = await fixture.EventStore.WriteSerializableEventsAsync(durableEvents).ConfigureAwait(false);
        Assert.True(writeResult.IsSuccess, writeResult.IsSuccess ? string.Empty : writeResult.GetException().Message);

        foreach (var serializableEvent in durableEvents.OrderByDescending(
                     item => item.SortableUniqueIdValue,
                     StringComparer.Ordinal))
        {
            _ = await fixture.Executor.ApplySerializableEventsAsync(host, [serializableEvent]).ConfigureAwait(false);
        }

        var sequentialInlineUpdatedRows = await CountUpdatedRowsAsync(fixture, projector.Rows.PhysicalName).ConfigureAwait(false);
        Assert.True(
            sequentialInlineUpdatedRows < 64,
            $"The old one-event inline mutant unexpectedly retained every update ({sequentialInlineUpdatedRows}/64).");

        static async Task<int> CountUpdatedRowsAsync(MultiProviderFixtureBase provider, string tableName)
        {
            await using var connection = await provider.OpenConnectionAsync().ConfigureAwait(false);
            return await connection.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {tableName} WHERE location LIKE '%-U';")
                .ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<SerializableEvent> CreateFixedAgedDescendingPairs(MultiProviderFixtureBase fixture)
    {
        var fixedTimestamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        return Enumerable.Range(0, 64)
            .SelectMany(pair =>
            {
                var forecastId = StableGuid(pair * 2 + 1);
                var createdId = StableGuid(pair * 2 + 1);
                var updatedId = StableGuid(pair * 2 + 2);
                var created = new Event(
                    new WeatherForecastCreated(
                        forecastId,
                        $"Loc-{pair:D3}",
                        new DateOnly(2024, 1, 2).AddDays(pair % 7),
                        20 + pair % 10,
                        $"Forecast-{pair:D3}"),
                    SortableUniqueId.Generate(fixedTimestamp.AddTicks(pair * 2L), createdId),
                    nameof(WeatherForecastCreated),
                    createdId,
                    new EventMetadata("g57-ac8", "g57-ac8", "test"),
                    []);
                var updated = new Event(
                    new WeatherForecastUpdated(
                        forecastId,
                        $"Loc-{pair:D3}-U",
                        new DateOnly(2024, 1, 2).AddDays(pair % 7),
                        20 + pair % 10,
                        $"Forecast-{pair:D3}"),
                    SortableUniqueId.Generate(fixedTimestamp.AddTicks(pair * 2L + 1), updatedId),
                    nameof(WeatherForecastUpdated),
                    updatedId,
                    new EventMetadata("g57-ac8", "g57-ac8", "test"),
                    []);
                return new[]
                {
                    created.ToSerializableEvent(fixture.DomainTypes.EventTypes),
                    updated.ToSerializableEvent(fixture.DomainTypes.EventTypes)
                };
            })
            .ToList();
    }

    private static Guid StableGuid(int ordinal) =>
        Guid.Parse($"00000000-0000-4000-8000-{ordinal:D12}");

    public static async Task AssertProviderWorksAsync(MultiProviderFixtureBase fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");

        await fixture.ResetAsync().ConfigureAwait(false);

        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        await fixture.Executor.InitializeAsync(new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo().DatabaseType))
            .ConfigureAwait(false);
        var registryStore = fixture.Services.GetRequiredService<IMvRegistryStore>();
        var initialRegistryEntries = await registryStore.GetEntriesAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName,
                projector.ViewVersion)
            .ConfigureAwait(false);
        Assert.NotEmpty(initialRegistryEntries);
        Assert.Null(await registryStore.GetActiveAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName)
            .ConfigureAwait(false));
        Assert.All(initialRegistryEntries, entry => Assert.True(entry.CurrentCheckpointTruth.IsUnknown));

        var emptyCatchUp = await fixture.Executor.CatchUpOnceAsync(
                new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo().DatabaseType))
            .ConfigureAwait(false);
        var emptyHistoryEntries = await registryStore.GetEntriesAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName,
                projector.ViewVersion)
            .ConfigureAwait(false);
        Assert.Equal(0, emptyCatchUp.AppliedEvents);
        Assert.All(emptyHistoryEntries, entry =>
        {
            Assert.True(entry.CurrentCheckpointTruth.IsKnownZero);
            Assert.False(entry.CurrentCheckpointTruth.IsUnknown);
        });

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var forecastId = Guid.CreateVersion7();
        var forecastDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Tokyo",
            Date = forecastDate,
            TemperatureC = 20,
            Summary = "Sunny"
        }).ConfigureAwait(false);

        await executor.ExecuteAsync(new UpdateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Kyoto",
            Date = forecastDate.AddDays(1),
            TemperatureC = 21,
            Summary = "Cloudy"
        }).ConfigureAwait(false);

        await executor.ExecuteAsync(new DeleteWeatherForecast
        {
            ForecastId = forecastId
        }).ConfigureAwait(false);

        var firstCatchUp = await fixture.Executor.CatchUpOnceAsync(
            new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo().DatabaseType))
            .ConfigureAwait(false);
        var secondCatchUp = await fixture.Executor.CatchUpOnceAsync(
            new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo().DatabaseType))
            .ConfigureAwait(false);
        var finalRegistryEntries = await registryStore.GetEntriesAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName,
                projector.ViewVersion)
            .ConfigureAwait(false);
        Assert.All(finalRegistryEntries, entry => Assert.True(entry.CurrentCheckpointTruth.IsKnown));

        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        var row = await connection.QuerySingleAsync<ForecastDbRow>(
            $"""
             SELECT forecast_id AS ForecastId,
                    location AS Location,
                    is_deleted AS IsDeleted,
                    _last_sortable_unique_id AS LastSortableUniqueId
             FROM {MultiProviderFixtureBase.ForecastTable}
             WHERE forecast_id = @ForecastId;
             """,
            new { ForecastId = forecastId.ToString("D") }).ConfigureAwait(false);
        var registry = await connection.QuerySingleAsync<RegistryDbRow>(
            """
            SELECT applied_event_version AS AppliedEventVersion,
                   current_position AS CurrentPosition,
                   last_applied_source AS LastAppliedSource
            FROM sekiban_mv_registry
            WHERE view_name = 'WeatherForecastPortable'
              AND logical_table = 'forecasts';
            """).ConfigureAwait(false);
        var activeVersion = await connection.ExecuteScalarAsync<int>(
            """
            SELECT active_version
            FROM sekiban_mv_active
            WHERE view_name = 'WeatherForecastPortable';
            """).ConfigureAwait(false);

        Assert.Equal(3, firstCatchUp.AppliedEvents);
        Assert.Equal(0, secondCatchUp.AppliedEvents);
        Assert.Equal("Kyoto", row.Location);
        Assert.True(row.IsDeleted);
        Assert.False(string.IsNullOrWhiteSpace(row.LastSortableUniqueId));
        Assert.Equal(3, registry.AppliedEventVersion);
        Assert.Equal("catchup", registry.LastAppliedSource);
        Assert.False(string.IsNullOrWhiteSpace(registry.CurrentPosition));
        Assert.Equal(1, activeVersion);
    }

    public static async Task AssertServiceIsolationAsync(SqliteMvFixture fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");

        await fixture.ResetAsync().ConfigureAwait(false);

        var sourceFactory = fixture.EventStoreFactory;
        var serviceA = $"orders-{Guid.NewGuid():N}";
        var serviceB = $"billing-{Guid.NewGuid():N}";
        var storeA = sourceFactory.CreateForService(serviceA);
        var storeB = sourceFactory.CreateForService(serviceB);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var host = new NativeMvApplyHost(
            projector,
            fixture.DomainTypes.EventTypes,
            MvDbType.Sqlite);
        var registry = fixture.Services.GetRequiredService<IMvRegistryStore>();
        var serviceAExecutor = new SqliteMvExecutor(
            sourceFactory,
            registry,
            Options.Create(new MvOptions
            {
                ServiceId = serviceA,
                BatchSize = 100,
                SafeWindowMs = 0
            }),
            NullLogger<SqliteMvExecutor>.Instance,
            fixture.ConnectionStringForTests);
        var serviceBExecutor = new SqliteMvExecutor(
            sourceFactory,
            registry,
            Options.Create(new MvOptions
            {
                ServiceId = serviceB,
                BatchSize = 100,
                SafeWindowMs = 0
            }),
            NullLogger<SqliteMvExecutor>.Instance,
            fixture.ConnectionStringForTests);

        var forecastA = Guid.CreateVersion7();
        var forecastB = Guid.CreateVersion7();
        await storeA.WriteSerializableEventsAsync(
            [CreateForecastEvent(forecastA, "orders-location", DateTimeOffset.UtcNow.AddSeconds(-2), fixture)]).ConfigureAwait(false);
        await storeB.WriteSerializableEventsAsync(
            [CreateForecastEvent(forecastB, "billing-location", DateTimeOffset.UtcNow.AddSeconds(-1), fixture)]).ConfigureAwait(false);

        await serviceAExecutor.InitializeAsync(host).ConfigureAwait(false);
        var serviceAResult = await serviceAExecutor.CatchUpOnceAsync(host).ConfigureAwait(false);

        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            var serviceARowCount = await connection.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM {MultiProviderFixtureBase.ForecastTable} WHERE forecast_id = @ForecastId;",
                new { ForecastId = forecastA.ToString("D") }).ConfigureAwait(false);
            var serviceBRowCount = await connection.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM {MultiProviderFixtureBase.ForecastTable} WHERE forecast_id = @ForecastId;",
                new { ForecastId = forecastB.ToString("D") }).ConfigureAwait(false);

            Assert.Equal(1, serviceAResult.AppliedEvents);
            Assert.Equal(1, serviceARowCount);
            Assert.Equal(0, serviceBRowCount);
        }

        await serviceBExecutor.InitializeAsync(host).ConfigureAwait(false);
        var serviceBResult = await serviceBExecutor.CatchUpOnceAsync(host).ConfigureAwait(false);

        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            var serviceBRowCount = await connection.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM {MultiProviderFixtureBase.ForecastTable} WHERE forecast_id = @ForecastId;",
                new { ForecastId = forecastB.ToString("D") }).ConfigureAwait(false);

            Assert.Equal(1, serviceBResult.AppliedEvents);
            Assert.Equal(1, serviceBRowCount);
        }
    }

    private static SerializableEvent CreateForecastEvent(
        Guid forecastId,
        string location,
        DateTimeOffset timestamp,
        MultiProviderFixtureBase fixture)
    {
        var eventId = Guid.CreateVersion7();
        var evt = new Event(
            new WeatherForecastCreated(
                forecastId,
                location,
                DateOnly.FromDateTime(timestamp.UtcDateTime),
                20,
                "Sunny"),
            SortableUniqueId.Generate(timestamp.UtcDateTime, eventId),
            nameof(WeatherForecastCreated),
            eventId,
            new EventMetadata("service-scope-test", "service-scope-test", "test"),
            []);
        return evt.ToSerializableEvent(fixture.DomainTypes.EventTypes);
    }
}
