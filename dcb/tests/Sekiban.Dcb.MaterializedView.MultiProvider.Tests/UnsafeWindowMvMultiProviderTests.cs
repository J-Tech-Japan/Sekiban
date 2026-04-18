using System.Data.Common;
using Dapper;
using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.MaterializedViews;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

// ============================================================================
// Unsafe Window Materialized View — provider parity integration tests
// (issue #1035).
//
// Each provider fixture already spins up a Testcontainers-backed MySQL / SQL
// Server instance or a file-backed SQLite database. We reuse those fixtures
// here so the tests run automatically on the same CI pipeline as the classic
// MV multi-provider tests. The event-source side is driven by a command
// executor against the fixture's in-memory event store; the UWMV side is
// driven directly by each provider's initializer / catch-up worker / promoter.
// The same WeatherForecast projector definition is used for every provider
// so any dialect mistakes surface immediately as a DDL or SQL error.
// ============================================================================

[Collection(nameof(SqlServerMvCollection))]
public sealed class UnsafeWindowMvSqlServerIntegrationTests(SqlServerMvFixture fixture)
{
    [SkippableFact]
    public Task InitializeCatchUpAndPromote_ProducesExpectedSafeAndDeleteBehavior() =>
        UnsafeWindowMultiProviderAssertions.AssertAsync(
            fixture,
            new SqlServerUnsafeWindowHarness(fixture.ConnectionStringForTests, fixture));
}

[Collection(nameof(MySqlMvCollection))]
public sealed class UnsafeWindowMvMySqlIntegrationTests(MySqlMvFixture fixture)
{
    [SkippableFact]
    public Task InitializeCatchUpAndPromote_ProducesExpectedSafeAndDeleteBehavior() =>
        UnsafeWindowMultiProviderAssertions.AssertAsync(
            fixture,
            new MySqlUnsafeWindowHarness(fixture.ConnectionStringForTests, fixture));
}

[Collection(nameof(SqliteMvCollection))]
public sealed class UnsafeWindowMvSqliteIntegrationTests(SqliteMvFixture fixture)
{
    [SkippableFact]
    public Task InitializeCatchUpAndPromote_ProducesExpectedSafeAndDeleteBehavior() =>
        UnsafeWindowMultiProviderAssertions.AssertAsync(
            fixture,
            new SqliteUnsafeWindowHarness(fixture.ConnectionStringForTests, fixture));
}

internal interface IUnsafeWindowTestHarness : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken ct);
    Task<int> CatchUpOnceAsync(CancellationToken ct);
    Task<int> PromoteOnceAsync(CancellationToken ct);
    string SafeTable { get; }
    string UnsafeTable { get; }
    string CurrentLiveView { get; }
    Task<DbConnection> OpenConnectionAsync();
    Task ResetTablesAsync();
}

internal static class UnsafeWindowMultiProviderAssertions
{
    public static async Task AssertAsync(MultiProviderFixtureBase fixture, IUnsafeWindowTestHarness harness)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Provider fixture is unavailable.");

        await using (harness)
        {
            fixture.EventStore.Clear();
            await harness.InitializeAsync(CancellationToken.None);
            await harness.ResetTablesAsync();

            var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
            var forecastId = Guid.CreateVersion7();

            await executor.ExecuteAsync(new CreateWeatherForecast
            {
                ForecastId = forecastId,
                Location = "Tokyo",
                Date = new DateOnly(2026, 4, 18),
                TemperatureC = 20,
                Summary = "Sunny"
            });
            await executor.ExecuteAsync(new ChangeLocationName
            {
                ForecastId = forecastId,
                NewLocationName = "Tokyo-Shinjuku"
            });

            // Catch up both events into unsafe.
            var applied = await harness.CatchUpOnceAsync(CancellationToken.None);
            Assert.Equal(2, applied);

            await using (var connection = await harness.OpenConnectionAsync())
            {
                var unsafeLocation = await connection.ExecuteScalarAsync<string>(
                    $"SELECT location FROM {harness.UnsafeTable} WHERE _projection_key = @Key;",
                    new { Key = forecastId.ToString() });
                Assert.Equal("Tokyo-Shinjuku", unsafeLocation);

                var safeCount = await connection.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM {harness.SafeTable};");
                Assert.Equal(0, safeCount);

                var liveLocation = await connection.ExecuteScalarAsync<string>(
                    $"SELECT location FROM {harness.CurrentLiveView} WHERE _projection_key = @Key;",
                    new { Key = forecastId.ToString() });
                Assert.Equal("Tokyo-Shinjuku", liveLocation);
            }

            // Wait for the projector's 2-second safe window, then promote.
            await Task.Delay(TimeSpan.FromMilliseconds(2200));
            var promoted = await harness.PromoteOnceAsync(CancellationToken.None);
            Assert.Equal(1, promoted);

            await using (var connection = await harness.OpenConnectionAsync())
            {
                var safeLocation = await connection.ExecuteScalarAsync<string>(
                    $"SELECT location FROM {harness.SafeTable} WHERE _projection_key = @Key;",
                    new { Key = forecastId.ToString() });
                Assert.Equal("Tokyo-Shinjuku", safeLocation);

                var unsafeCount = await connection.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM {harness.UnsafeTable};");
                Assert.Equal(0, unsafeCount);

                var liveCount = await connection.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM {harness.CurrentLiveView} WHERE _projection_key = @Key;",
                    new { Key = forecastId.ToString() });
                Assert.Equal(1, liveCount);
            }

            // Delete the forecast; it reappears in unsafe as a tombstone.
            await executor.ExecuteAsync(new DeleteWeatherForecast { ForecastId = forecastId });
            var appliedDelete = await harness.CatchUpOnceAsync(CancellationToken.None);
            Assert.Equal(1, appliedDelete);

            await using (var connection = await harness.OpenConnectionAsync())
            {
                var isDeleted = ToBool(await connection.ExecuteScalarAsync<object>(
                    $"SELECT _is_deleted FROM {harness.UnsafeTable} WHERE _projection_key = @Key;",
                    new { Key = forecastId.ToString() }));
                Assert.True(isDeleted);

                var liveCount = await connection.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM {harness.CurrentLiveView} WHERE _projection_key = @Key;",
                    new { Key = forecastId.ToString() });
                Assert.Equal(0, liveCount);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(2200));
            var promoted2 = await harness.PromoteOnceAsync(CancellationToken.None);
            Assert.Equal(1, promoted2);

            await using (var connection = await harness.OpenConnectionAsync())
            {
                var isDeletedSafe = ToBool(await connection.ExecuteScalarAsync<object>(
                    $"SELECT _is_deleted FROM {harness.SafeTable} WHERE _projection_key = @Key;",
                    new { Key = forecastId.ToString() }));
                Assert.True(isDeletedSafe);

                var liveCount = await connection.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM {harness.CurrentLiveView} WHERE _projection_key = @Key;",
                    new { Key = forecastId.ToString() });
                Assert.Equal(0, liveCount);
            }
        }
    }

    private static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        long l => l != 0,
        int i => i != 0,
        short s => s != 0,
        byte b8 => b8 != 0,
        sbyte sb => sb != 0,
        _ => Convert.ToBoolean(value)
    };
}

// -- Provider test harnesses --

internal sealed class SqlServerUnsafeWindowHarness : IUnsafeWindowTestHarness
{
    private readonly WeatherForecastUnsafeWindowMvV1 _projector = new();
    private readonly string _connectionString;
    private readonly UnsafeWindowMvSqlServerSchemaResolver _resolver;
    private readonly UnsafeWindowMvSqlServerInitializer _initializer;
    private readonly UnsafeWindowMvSqlServerCatchUpWorker<WeatherForecastUnsafeRow> _catchUp;
    private readonly UnsafeWindowMvSqlServerPromoter<WeatherForecastUnsafeRow> _promoter;

    public SqlServerUnsafeWindowHarness(string connectionString, MultiProviderFixtureBase fixture)
    {
        _connectionString = connectionString;
        _resolver = new UnsafeWindowMvSqlServerSchemaResolver(_projector.ViewName, _projector.ViewVersion, _projector.Schema);
        _initializer = new UnsafeWindowMvSqlServerInitializer(_resolver, connectionString, NullLogger.Instance);
        _catchUp = new UnsafeWindowMvSqlServerCatchUpWorker<WeatherForecastUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
        _promoter = new UnsafeWindowMvSqlServerPromoter<WeatherForecastUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
    }

    public Task InitializeAsync(CancellationToken ct) => _initializer.InitializeAsync(ct);
    public Task<int> CatchUpOnceAsync(CancellationToken ct) => _catchUp.CatchUpOnceAsync(ct);
    public Task<int> PromoteOnceAsync(CancellationToken ct) => _promoter.PromoteOnceAsync(ct);
    public string SafeTable => _resolver.SafeTable;
    public string UnsafeTable => _resolver.UnsafeTable;
    public string CurrentLiveView => _resolver.CurrentLiveView;

    public async Task<DbConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task ResetTablesAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await connection.ExecuteAsync($"""
            IF OBJECT_ID(N'{_resolver.CurrentLiveView}', N'V') IS NOT NULL DROP VIEW {_resolver.CurrentLiveView};
            IF OBJECT_ID(N'{_resolver.CurrentView}', N'V') IS NOT NULL DROP VIEW {_resolver.CurrentView};
            IF OBJECT_ID(N'{_resolver.UnsafeTable}', N'U') IS NOT NULL DROP TABLE {_resolver.UnsafeTable};
            IF OBJECT_ID(N'{_resolver.SafeTable}', N'U') IS NOT NULL DROP TABLE {_resolver.SafeTable};
            """);
        await _initializer.InitializeAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class MySqlUnsafeWindowHarness : IUnsafeWindowTestHarness
{
    private readonly WeatherForecastUnsafeWindowMvV1 _projector = new();
    private readonly string _connectionString;
    private readonly UnsafeWindowMvMySqlSchemaResolver _resolver;
    private readonly UnsafeWindowMvMySqlInitializer _initializer;
    private readonly UnsafeWindowMvMySqlCatchUpWorker<WeatherForecastUnsafeRow> _catchUp;
    private readonly UnsafeWindowMvMySqlPromoter<WeatherForecastUnsafeRow> _promoter;

    public MySqlUnsafeWindowHarness(string connectionString, MultiProviderFixtureBase fixture)
    {
        _connectionString = connectionString;
        _resolver = new UnsafeWindowMvMySqlSchemaResolver(_projector.ViewName, _projector.ViewVersion, _projector.Schema);
        _initializer = new UnsafeWindowMvMySqlInitializer(_resolver, connectionString, NullLogger.Instance);
        _catchUp = new UnsafeWindowMvMySqlCatchUpWorker<WeatherForecastUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
        _promoter = new UnsafeWindowMvMySqlPromoter<WeatherForecastUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
    }

    public Task InitializeAsync(CancellationToken ct) => _initializer.InitializeAsync(ct);
    public Task<int> CatchUpOnceAsync(CancellationToken ct) => _catchUp.CatchUpOnceAsync(ct);
    public Task<int> PromoteOnceAsync(CancellationToken ct) => _promoter.PromoteOnceAsync(ct);
    public string SafeTable => _resolver.SafeTable;
    public string UnsafeTable => _resolver.UnsafeTable;
    public string CurrentLiveView => _resolver.CurrentLiveView;

    public async Task<DbConnection> OpenConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class SqliteUnsafeWindowHarness : IUnsafeWindowTestHarness
{
    private readonly WeatherForecastUnsafeWindowMvV1 _projector = new();
    private readonly string _connectionString;
    private readonly UnsafeWindowMvSqliteSchemaResolver _resolver;
    private readonly UnsafeWindowMvSqliteInitializer _initializer;
    private readonly UnsafeWindowMvSqliteCatchUpWorker<WeatherForecastUnsafeRow> _catchUp;
    private readonly UnsafeWindowMvSqlitePromoter<WeatherForecastUnsafeRow> _promoter;

    public SqliteUnsafeWindowHarness(string connectionString, MultiProviderFixtureBase fixture)
    {
        _connectionString = connectionString;
        _resolver = new UnsafeWindowMvSqliteSchemaResolver(_projector.ViewName, _projector.ViewVersion, _projector.Schema);
        _initializer = new UnsafeWindowMvSqliteInitializer(_resolver, connectionString, NullLogger.Instance);
        _catchUp = new UnsafeWindowMvSqliteCatchUpWorker<WeatherForecastUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
        _promoter = new UnsafeWindowMvSqlitePromoter<WeatherForecastUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
    }

    public Task InitializeAsync(CancellationToken ct) => _initializer.InitializeAsync(ct);
    public Task<int> CatchUpOnceAsync(CancellationToken ct) => _catchUp.CatchUpOnceAsync(ct);
    public Task<int> PromoteOnceAsync(CancellationToken ct) => _promoter.PromoteOnceAsync(ct);
    public string SafeTable => _resolver.SafeTable;
    public string UnsafeTable => _resolver.UnsafeTable;
    public string CurrentLiveView => _resolver.CurrentLiveView;

    public async Task<DbConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
