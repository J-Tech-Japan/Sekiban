using System.Data.Common;
using Dapper;
using Dcb.Domain.WithoutResult.MaterializedViews;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Xunit;
using PgUwmv = Sekiban.Dcb.MaterializedView.Postgres;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

// ============================================================================
// Unsafe Window Materialized View — nullable business column regression tests.
//
// A projected row whose nullable column (WeatherForecast.summary, "TEXT NULL")
// is null must be written as SQL NULL on every write path of the runtime:
// unsafe upsert, safe promotion, mirror-safe-into-unsafe (reorder repair) and
// tombstone upsert. Binding the value as DBNull through Dapper's
// DynamicParameters throws NotSupportedException, which made the catch-up
// worker retry the same batch forever and never apply later events.
// ============================================================================

[Collection(nameof(PostgresMvCollection))]
public sealed class UnsafeWindowMvPostgresNullableColumnTests(PostgresMvFixture fixture)
{
    [SkippableFact]
    public Task NullableColumn_IsAppliedOnEveryWritePath() =>
        UnsafeWindowNullableColumnAssertions.AssertAsync(
            fixture,
            () => new PostgresUnsafeWindowHarness(fixture.ConnectionStringForTests, fixture));
}

[Collection(nameof(SqlServerMvCollection))]
public sealed class UnsafeWindowMvSqlServerNullableColumnTests(SqlServerMvFixture fixture)
{
    [SkippableFact]
    public Task NullableColumn_IsAppliedOnEveryWritePath() =>
        UnsafeWindowNullableColumnAssertions.AssertAsync(
            fixture,
            () => new SqlServerUnsafeWindowHarness(fixture.ConnectionStringForTests, fixture));
}

[Collection(nameof(MySqlMvCollection))]
public sealed class UnsafeWindowMvMySqlNullableColumnTests(MySqlMvFixture fixture)
{
    [SkippableFact]
    public Task NullableColumn_IsAppliedOnEveryWritePath() =>
        UnsafeWindowNullableColumnAssertions.AssertAsync(
            fixture,
            () => new MySqlUnsafeWindowHarness(fixture.ConnectionStringForTests, fixture));
}

[Collection(nameof(SqliteMvCollection))]
public sealed class UnsafeWindowMvSqliteNullableColumnTests(SqliteMvFixture fixture)
{
    [SkippableFact]
    public Task NullableColumn_IsAppliedOnEveryWritePath() =>
        UnsafeWindowNullableColumnAssertions.AssertAsync(
            fixture,
            () => new SqliteUnsafeWindowHarness(fixture.ConnectionStringForTests, fixture));
}

internal static class UnsafeWindowNullableColumnAssertions
{
    private static readonly TimeSpan PastSafeWindow = TimeSpan.FromMilliseconds(2200);

    public static async Task AssertAsync(
        MultiProviderFixtureBase fixture,
        Func<IUnsafeWindowTestHarness> createHarness)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Provider fixture is unavailable.");

        fixture.EventStore.Clear();
        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var nullId = Guid.CreateVersion7();
        var laterId = Guid.CreateVersion7();

        await using (var harness = createHarness())
        {
            await harness.InitializeAsync(CancellationToken.None);
            await harness.ResetTablesAsync();

            // Unsafe upsert path with a null nullable column.
            await executor.ExecuteAsync(new CreateWeatherForecast
            {
                ForecastId = nullId,
                Location = "Osaka",
                Date = new DateOnly(2026, 11, 17),
                TemperatureC = 12,
                Summary = null
            });
            Assert.Equal(1, await harness.CatchUpOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.UnsafeTable, nullId, "Osaka", expectDeleted: false);

            // A later event for the same key must still apply.
            await executor.ExecuteAsync(new ChangeLocationName { ForecastId = nullId, NewLocationName = "Osaka-Kita" });
            Assert.Equal(1, await harness.CatchUpOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.UnsafeTable, nullId, "Osaka-Kita", expectDeleted: false);
        }

        // Restart: fresh worker instances resume from the stored tables.
        await using (var harness = createHarness())
        {
            await harness.InitializeAsync(CancellationToken.None);
            Assert.Equal(0, await harness.CatchUpOnceAsync(CancellationToken.None));

            await executor.ExecuteAsync(new CreateWeatherForecast
            {
                ForecastId = laterId,
                Location = "Sapporo",
                Date = new DateOnly(2026, 11, 18),
                TemperatureC = 1,
                Summary = null
            });
            Assert.Equal(1, await harness.CatchUpOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.UnsafeTable, laterId, "Sapporo", expectDeleted: false);

            // Safe promotion path.
            await Task.Delay(PastSafeWindow);
            Assert.Equal(2, await harness.PromoteOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.SafeTable, nullId, "Osaka-Kita", expectDeleted: false);
            await AssertRowAsync(harness, harness.SafeTable, laterId, "Sapporo", expectDeleted: false);
            await AssertCountAsync(harness, harness.UnsafeTable, 0);

            // Reorder repair path: re-delivering an event older than the safe
            // row mirrors safe into unsafe and marks the key for rebuild.
            var createdEvent = await FindCreatedEventAsync(fixture, nullId);
            await harness.ApplyEventDirectlyAsync(createdEvent, CancellationToken.None);
            await AssertRowAsync(harness, harness.UnsafeTable, nullId, "Osaka-Kita", expectDeleted: false);
            await using (var connection = await harness.OpenConnectionAsync())
            {
                var needsRebuild = await connection.ExecuteScalarAsync<object>(
                    $"SELECT _needs_rebuild FROM {harness.UnsafeTable} WHERE _projection_key = @Key;",
                    new { Key = nullId.ToString() });
                Assert.True(ToBool(needsRebuild));
            }

            // Full-replay promotion of the rebuild-marked key.
            await Task.Delay(PastSafeWindow);
            Assert.Equal(1, await harness.PromoteOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.SafeTable, nullId, "Osaka-Kita", expectDeleted: false);
            await AssertCountAsync(harness, harness.UnsafeTable, 0);

            // Tombstone path, folding against the safe row (unsafe is empty).
            await executor.ExecuteAsync(new DeleteWeatherForecast { ForecastId = nullId });
            Assert.Equal(1, await harness.CatchUpOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.UnsafeTable, nullId, "Osaka-Kita", expectDeleted: true);

            await Task.Delay(PastSafeWindow);
            Assert.Equal(1, await harness.PromoteOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.SafeTable, nullId, "Osaka-Kita", expectDeleted: true);
            await AssertCountAsync(harness, harness.CurrentLiveView, 1);
        }
    }

    private static async Task<Event> FindCreatedEventAsync(MultiProviderFixtureBase fixture, Guid forecastId)
    {
        var serialized = await fixture.EventStore.ReadAllSerializableEventsAsync();
        return serialized
            .Select(e => e.ToEvent(fixture.DomainTypes.EventTypes))
            .Single(e => e.Payload is WeatherForecastCreated created && created.ForecastId == forecastId);
    }

    private static async Task AssertRowAsync(
        IUnsafeWindowTestHarness harness,
        string table,
        Guid forecastId,
        string expectedLocation,
        bool expectDeleted)
    {
        await using var connection = await harness.OpenConnectionAsync();
        var row = (IDictionary<string, object?>?)await connection.QueryFirstOrDefaultAsync(
            $"SELECT location, summary, _is_deleted FROM {table} WHERE _projection_key = @Key;",
            new { Key = forecastId.ToString() });
        Assert.NotNull(row);
        Assert.Equal(expectedLocation, row["location"]?.ToString());
        Assert.True(row["summary"] is null or DBNull, $"summary should be NULL in {table}");
        Assert.Equal(expectDeleted, ToBool(row["_is_deleted"]));
    }

    private static async Task AssertCountAsync(IUnsafeWindowTestHarness harness, string table, long expected)
    {
        await using var connection = await harness.OpenConnectionAsync();
        var count = await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table};");
        Assert.Equal(expected, count);
    }

    private static bool ToBool(object? value) => value switch
    {
        null or DBNull => false,
        bool b => b,
        long l => l != 0,
        int i => i != 0,
        short s => s != 0,
        byte b8 => b8 != 0,
        sbyte sb => sb != 0,
        ulong ul => ul != 0,
        _ => Convert.ToBoolean(value)
    };
}

internal sealed class PostgresUnsafeWindowHarness : IUnsafeWindowTestHarness
{
    private readonly WeatherForecastUnsafeWindowMvV1 _projector = new();
    private readonly string _connectionString;
    private readonly PgUwmv.UnsafeWindowMvSchemaResolver _resolver;
    private readonly PgUwmv.UnsafeWindowMvInitializer _initializer;
    private readonly PgUwmv.UnsafeWindowMvCatchUpWorker<WeatherForecastUnsafeRow> _catchUp;
    private readonly PgUwmv.UnsafeWindowMvPromoter<WeatherForecastUnsafeRow> _promoter;

    public PostgresUnsafeWindowHarness(string connectionString, MultiProviderFixtureBase fixture)
    {
        _connectionString = connectionString;
        _resolver = new PgUwmv.UnsafeWindowMvSchemaResolver(_projector.ViewName, _projector.ViewVersion, _projector.Schema);
        _initializer = new PgUwmv.UnsafeWindowMvInitializer(_resolver, connectionString, NullLogger.Instance);
        _catchUp = new PgUwmv.UnsafeWindowMvCatchUpWorker<WeatherForecastUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
        _promoter = new PgUwmv.UnsafeWindowMvPromoter<WeatherForecastUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
    }

    public Task InitializeAsync(CancellationToken ct) => _initializer.InitializeAsync(ct);
    public Task<int> CatchUpOnceAsync(CancellationToken ct) => _catchUp.CatchUpOnceAsync(ct);
    public Task<int> PromoteOnceAsync(CancellationToken ct) => _promoter.PromoteOnceAsync(ct);
    public string SafeTable => _resolver.SafeTable;
    public string UnsafeTable => _resolver.UnsafeTable;
    public string CurrentLiveView => _resolver.CurrentLiveView;

    public async Task<DbConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task ResetTablesAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await connection.ExecuteAsync($"""
            DROP VIEW IF EXISTS {_resolver.CurrentLiveView};
            DROP VIEW IF EXISTS {_resolver.CurrentView};
            DROP TABLE IF EXISTS {_resolver.UnsafeTable};
            DROP TABLE IF EXISTS {_resolver.SafeTable};
            """);
        await _initializer.InitializeAsync(CancellationToken.None);
    }

    public async Task ApplyEventDirectlyAsync(Event ev, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await new PgUwmv.UnsafeWindowMvStreamApplier<WeatherForecastUnsafeRow>(_resolver, _projector)
            .ApplyEventAsync(connection, transaction, ev, ct);
        await transaction.CommitAsync(ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
