using System.Data;
using System.Text;
using Dapper;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.Tags;
using Xunit;
using PgUwmv = Sekiban.Dcb.MaterializedView.Postgres;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

// ============================================================================
// Unsafe Window Materialized View — provider-specific column types on every
// write path (unsafe upsert, safe promotion, reorder mirror + rebuild,
// tombstone):
//   * SQL Server: a null value for a nullable VARBINARY(MAX) column must be
//     bound as a binary null (an untyped null is NVARCHAR, error 257).
//   * Postgres: a populated JSONB column must survive the reorder mirror
//     (safe -> unsafe) copy; the value is read back from Npgsql as a string
//     and must not be re-bound as text (text -> jsonb is not assignable).
// ============================================================================

[Collection(nameof(SqlServerMvCollection))]
public sealed class UnsafeWindowMvSqlServerBinaryColumnTests(SqlServerMvFixture fixture)
{
    [SkippableFact]
    public Task NullVarbinaryColumn_IsAppliedOnEveryWritePath() =>
        UnsafeWindowTypedColumnAssertions.AssertAsync(
            fixture,
            new SqlServerPayloadHarness(fixture.ConnectionStringForTests, fixture, new PayloadUnsafeWindowMv(
                "UwmvPayloadBinary",
                "VARBINARY(MAX) NULL",
                summary => summary is null ? null : Encoding.UTF8.GetBytes(summary),
                payload => payload)),
            summary: null,
            assertPayload: Assert.Null);

    [SkippableFact]
    public Task PopulatedVarbinaryColumn_IsAppliedOnEveryWritePath() =>
        UnsafeWindowTypedColumnAssertions.AssertAsync(
            fixture,
            new SqlServerPayloadHarness(fixture.ConnectionStringForTests, fixture, new PayloadUnsafeWindowMv(
                "UwmvPayloadBinary",
                "VARBINARY(MAX) NULL",
                summary => summary is null ? null : Encoding.UTF8.GetBytes(summary),
                payload => payload)),
            summary: "Cloudy",
            assertPayload: value => Assert.Equal(Encoding.UTF8.GetBytes("Cloudy"), Assert.IsType<byte[]>(value)));
}

[Collection(nameof(PostgresMvCollection))]
public sealed class UnsafeWindowMvPostgresJsonbColumnTests(PostgresMvFixture fixture)
{
    static UnsafeWindowMvPostgresJsonbColumnTests() => SqlMapper.AddTypeHandler(new PgJsonbHandler());

    [SkippableFact]
    public Task PopulatedJsonbColumn_IsAppliedOnEveryWritePath() =>
        UnsafeWindowTypedColumnAssertions.AssertAsync(
            fixture,
            new PostgresPayloadHarness(fixture.ConnectionStringForTests, fixture, new PayloadUnsafeWindowMv(
                "UwmvPayloadJsonb",
                "JSONB NULL",
                summary => summary is null ? null : $"{{\"summary\": \"{summary}\"}}",
                payload => payload is string json ? new PgJsonb(json) : payload)),
            summary: "Cloudy",
            assertPayload: value => Assert.Contains("\"Cloudy\"", Assert.IsType<string>(value)));

    [SkippableFact]
    public Task NullJsonbColumn_IsAppliedOnEveryWritePath() =>
        UnsafeWindowTypedColumnAssertions.AssertAsync(
            fixture,
            new PostgresPayloadHarness(fixture.ConnectionStringForTests, fixture, new PayloadUnsafeWindowMv(
                "UwmvPayloadJsonb",
                "JSONB NULL",
                summary => summary is null ? null : $"{{\"summary\": \"{summary}\"}}",
                payload => payload is string json ? new PgJsonb(json) : payload)),
            summary: null,
            assertPayload: Assert.Null);
}

internal static class UnsafeWindowTypedColumnAssertions
{
    private static readonly TimeSpan PastSafeWindow = TimeSpan.FromMilliseconds(1200);

    public static async Task AssertAsync(
        MultiProviderFixtureBase fixture,
        IUnsafeWindowTestHarness harness,
        string? summary,
        Action<object?> assertPayload)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Provider fixture is unavailable.");

        await using (harness)
        {
            fixture.EventStore.Clear();
            await harness.InitializeAsync(CancellationToken.None);
            await harness.ResetTablesAsync();

            var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
            var forecastId = Guid.CreateVersion7();

            // Unsafe upsert.
            await executor.ExecuteAsync(new CreateWeatherForecast
            {
                ForecastId = forecastId,
                Location = "Nagoya",
                Date = new DateOnly(2026, 11, 17),
                TemperatureC = 10,
                Summary = summary
            });
            Assert.Equal(1, await harness.CatchUpOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.UnsafeTable, forecastId, expectDeleted: false, assertPayload);

            // Safe promotion.
            await Task.Delay(PastSafeWindow);
            Assert.Equal(1, await harness.PromoteOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.SafeTable, forecastId, expectDeleted: false, assertPayload);

            // Reorder mirror (safe -> unsafe) and full-replay rebuild.
            var created = (await fixture.EventStore.ReadAllSerializableEventsAsync())
                .Select(e => e.ToEvent(fixture.DomainTypes.EventTypes))
                .Single(e => e.Payload is WeatherForecastCreated c && c.ForecastId == forecastId);
            await harness.ApplyEventDirectlyAsync(created, CancellationToken.None);
            await AssertRowAsync(harness, harness.UnsafeTable, forecastId, expectDeleted: false, assertPayload);
            await Task.Delay(PastSafeWindow);
            Assert.Equal(1, await harness.PromoteOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.SafeTable, forecastId, expectDeleted: false, assertPayload);

            // Tombstone, then promotion.
            await executor.ExecuteAsync(new DeleteWeatherForecast { ForecastId = forecastId });
            Assert.Equal(1, await harness.CatchUpOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.UnsafeTable, forecastId, expectDeleted: true, assertPayload);
            await Task.Delay(PastSafeWindow);
            Assert.Equal(1, await harness.PromoteOnceAsync(CancellationToken.None));
            await AssertRowAsync(harness, harness.SafeTable, forecastId, expectDeleted: true, assertPayload);
        }
    }

    private static async Task AssertRowAsync(
        IUnsafeWindowTestHarness harness,
        string table,
        Guid forecastId,
        bool expectDeleted,
        Action<object?> assertPayload)
    {
        await using var connection = await harness.OpenConnectionAsync();
        var row = (IDictionary<string, object?>?)await connection.QueryFirstOrDefaultAsync(
            $"SELECT location, payload, _is_deleted FROM {table} WHERE _projection_key = @Key;",
            new { Key = forecastId.ToString() });
        Assert.NotNull(row);
        Assert.Equal("Nagoya", row["location"]?.ToString());
        assertPayload(row["payload"] is DBNull ? null : row["payload"]);
        Assert.Equal(expectDeleted, row["_is_deleted"] switch
        {
            bool b => b,
            null => false,
            var other => Convert.ToInt64(other) != 0
        });
    }
}

/// <summary>Weather forecast UWMV with one provider-specific nullable <c>payload</c> column.</summary>
internal sealed class PayloadUnsafeWindowMv : IUnsafeWindowMvProjector<PayloadUnsafeRow>
{
    private readonly Func<string?, object?> _encode;

    public PayloadUnsafeWindowMv(
        string viewName,
        string payloadSqlType,
        Func<string?, object?> encode,
        Func<object?, object?> toParameter)
    {
        ViewName = viewName;
        _encode = encode;
        Schema = new UnsafeWindowMvSchema(
            new UnsafeWindowMvColumn("forecast_id", "UUID NOT NULL", row => ((PayloadUnsafeRow)row).ForecastId),
            new UnsafeWindowMvColumn("location", "TEXT NOT NULL", row => ((PayloadUnsafeRow)row).Location),
            new UnsafeWindowMvColumn("payload", payloadSqlType, row => toParameter(((PayloadUnsafeRow)row).Payload)));
    }

    public string ViewName { get; }
    public int ViewVersion => 1;
    public TimeSpan SafeWindow => TimeSpan.FromSeconds(1);
    public UnsafeWindowMvSchema Schema { get; }

    public string? GetProjectionKey(Event ev) => ev.Payload switch
    {
        WeatherForecastCreated c => c.ForecastId.ToString(),
        WeatherForecastDeleted d => d.ForecastId.ToString(),
        _ => null
    };

    public UnsafeWindowMvApplyOutcome Apply(PayloadUnsafeRow? current, Event ev) => ev.Payload switch
    {
        WeatherForecastCreated c => new UnsafeWindowMvApplyOutcome.Upsert(
            c.ForecastId.ToString(),
            new PayloadUnsafeRow { ForecastId = c.ForecastId, Location = c.Location, Payload = _encode(c.Summary) }),
        WeatherForecastDeleted d => new UnsafeWindowMvApplyOutcome.Delete(d.ForecastId.ToString()),
        _ => new UnsafeWindowMvApplyOutcome.NoChange()
    };

    public IReadOnlyList<ITag> TagsForProjectionKey(string projectionKey) =>
        Guid.TryParse(projectionKey, out var id) ? [new WeatherForecastTag(id)] : [];
}

internal sealed record PayloadUnsafeRow
{
    public Guid ForecastId { get; init; }
    public string Location { get; init; } = string.Empty;
    public object? Payload { get; init; }
}

/// <summary>Application-side JSONB wrapper bound through a Dapper type handler.</summary>
internal sealed record PgJsonb(string Json);

internal sealed class PgJsonbHandler : SqlMapper.TypeHandler<PgJsonb>
{
    public override void SetValue(IDbDataParameter parameter, PgJsonb? value)
    {
        ((NpgsqlParameter)parameter).NpgsqlDbType = NpgsqlDbType.Jsonb;
        parameter.Value = value?.Json ?? (object)DBNull.Value;
    }

    public override PgJsonb? Parse(object value) => new((string)value);
}

internal sealed class SqlServerPayloadHarness : IUnsafeWindowTestHarness
{
    private readonly PayloadUnsafeWindowMv _projector;
    private readonly string _connectionString;
    private readonly UnsafeWindowMvSqlServerSchemaResolver _resolver;
    private readonly UnsafeWindowMvSqlServerInitializer _initializer;
    private readonly UnsafeWindowMvSqlServerCatchUpWorker<PayloadUnsafeRow> _catchUp;
    private readonly UnsafeWindowMvSqlServerPromoter<PayloadUnsafeRow> _promoter;

    public SqlServerPayloadHarness(string connectionString, MultiProviderFixtureBase fixture, PayloadUnsafeWindowMv projector)
    {
        _projector = projector;
        _connectionString = connectionString;
        _resolver = new UnsafeWindowMvSqlServerSchemaResolver(_projector.ViewName, _projector.ViewVersion, _projector.Schema);
        _initializer = new UnsafeWindowMvSqlServerInitializer(_resolver, connectionString, NullLogger.Instance);
        _catchUp = new UnsafeWindowMvSqlServerCatchUpWorker<PayloadUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
        _promoter = new UnsafeWindowMvSqlServerPromoter<PayloadUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
    }

    public Task InitializeAsync(CancellationToken ct) => _initializer.InitializeAsync(ct);
    public Task<int> CatchUpOnceAsync(CancellationToken ct) => _catchUp.CatchUpOnceAsync(ct);
    public Task<int> PromoteOnceAsync(CancellationToken ct) => _promoter.PromoteOnceAsync(ct);
    public string SafeTable => _resolver.SafeTable;
    public string UnsafeTable => _resolver.UnsafeTable;
    public string CurrentLiveView => _resolver.CurrentLiveView;

    public async Task<System.Data.Common.DbConnection> OpenConnectionAsync()
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

    public async Task ApplyEventDirectlyAsync(Event ev, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await new UnsafeWindowMvSqlServerStreamApplier<PayloadUnsafeRow>(_resolver, _projector)
            .ApplyEventAsync(connection, transaction, ev, ct);
        await transaction.CommitAsync(ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class PostgresPayloadHarness : IUnsafeWindowTestHarness
{
    private readonly PayloadUnsafeWindowMv _projector;
    private readonly string _connectionString;
    private readonly PgUwmv.UnsafeWindowMvSchemaResolver _resolver;
    private readonly PgUwmv.UnsafeWindowMvInitializer _initializer;
    private readonly PgUwmv.UnsafeWindowMvCatchUpWorker<PayloadUnsafeRow> _catchUp;
    private readonly PgUwmv.UnsafeWindowMvPromoter<PayloadUnsafeRow> _promoter;

    public PostgresPayloadHarness(string connectionString, MultiProviderFixtureBase fixture, PayloadUnsafeWindowMv projector)
    {
        _projector = projector;
        _connectionString = connectionString;
        _resolver = new PgUwmv.UnsafeWindowMvSchemaResolver(_projector.ViewName, _projector.ViewVersion, _projector.Schema);
        _initializer = new PgUwmv.UnsafeWindowMvInitializer(_resolver, connectionString, NullLogger.Instance);
        _catchUp = new PgUwmv.UnsafeWindowMvCatchUpWorker<PayloadUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
        _promoter = new PgUwmv.UnsafeWindowMvPromoter<PayloadUnsafeRow>(_resolver, _projector, fixture.EventStore, fixture.DomainTypes.EventTypes, connectionString, NullLogger.Instance);
    }

    public Task InitializeAsync(CancellationToken ct) => _initializer.InitializeAsync(ct);
    public Task<int> CatchUpOnceAsync(CancellationToken ct) => _catchUp.CatchUpOnceAsync(ct);
    public Task<int> PromoteOnceAsync(CancellationToken ct) => _promoter.PromoteOnceAsync(ct);
    public string SafeTable => _resolver.SafeTable;
    public string UnsafeTable => _resolver.UnsafeTable;
    public string CurrentLiveView => _resolver.CurrentLiveView;

    public async Task<System.Data.Common.DbConnection> OpenConnectionAsync()
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
        await new PgUwmv.UnsafeWindowMvStreamApplier<PayloadUnsafeRow>(_resolver, _projector)
            .ApplyEventAsync(connection, transaction, ev, ct);
        await transaction.CommitAsync(ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
