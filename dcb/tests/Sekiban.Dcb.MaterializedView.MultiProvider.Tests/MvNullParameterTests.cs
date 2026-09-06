using System.Data.Common;
using Dapper;
using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Xunit;
using Xunit.Abstractions;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvNullParameterTests(PostgresMvFixture fixture, ITestOutputHelper output)
{
    [SkippableFact]
    public Task NullableText_IsBoundAsSqlNull_AndAllQueryPortsExecute() =>
        MvNullParameterAssertions.AssertNullableTextAsync(fixture, output);

    [SkippableFact]
    public Task NonInferableScalarNull_IsMeasuredAtThePostgresQueryPort() =>
        MvNullParameterAssertions.AssertPostgresNonInferableScalarAsync(fixture, output);
}

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvNullParameterTests(MySqlMvFixture fixture, ITestOutputHelper output)
{
    [SkippableFact]
    public Task NullableText_IsBoundAsSqlNull_AndAllQueryPortsExecute() =>
        MvNullParameterAssertions.AssertNullableTextAsync(fixture, output);
}

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvNullParameterTests(SqlServerMvFixture fixture, ITestOutputHelper output)
{
    [SkippableFact]
    public Task NullableText_IsBoundAsSqlNull_AndAllQueryPortsExecute() =>
        MvNullParameterAssertions.AssertNullableTextAsync(fixture, output);
}

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvNullParameterTests(SqliteMvFixture fixture, ITestOutputHelper output)
{
    [SkippableFact]
    public Task NullableText_IsBoundAsSqlNull_AndAllQueryPortsExecute() =>
        MvNullParameterAssertions.AssertNullableTextAsync(fixture, output);
}

internal static class MvNullParameterAssertions
{
    public static async Task AssertNullableTextAsync(
        MultiProviderFixtureBase fixture,
        ITestOutputHelper output)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Materialized-view fixture is unavailable.");

        await fixture.ResetAsync().ConfigureAwait(false);
        var projector = new NullableTextMvProjector();
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
        await fixture.Executor.InitializeAsync(host).ConfigureAwait(false);

        output.WriteLine($"provider={fixture.DatabaseTypeForTests}");
        output.WriteLine($"version={await ReadProviderVersionAsync(fixture).ConfigureAwait(false)}");
        output.WriteLine($"inferable-query={NullableTextMvProjector.InferableTextQuery}");

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var forecastId = Guid.CreateVersion7();
        var forecastDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        await executor.ExecuteAsync(
                new CreateWeatherForecast
                {
                    ForecastId = forecastId,
                    Location = "Tokyo",
                    Date = forecastDate,
                    TemperatureC = 20,
                    Summary = "initial"
                })
            .ConfigureAwait(false);
        var firstCatchUp = await fixture.Executor.CatchUpOnceAsync(host).ConfigureAwait(false);
        var firstRow = await ReadRowAsync(fixture, projector, forecastId).ConfigureAwait(false);
        var firstEntry = await ReadRegistryEntryAsync(fixture, projector).ConfigureAwait(false);

        Assert.Equal(1, firstCatchUp.AppliedEvents);
        Assert.Equal("initial", firstRow.Summary);
        Assert.Equal(1, firstEntry.AppliedEventVersion);
        AssertCheckpointAdvanced(firstEntry);

        await executor.ExecuteAsync(
                new UpdateWeatherForecast
                {
                    ForecastId = forecastId,
                    Location = "Kyoto",
                    Date = forecastDate.AddDays(1),
                    TemperatureC = 21,
                    Summary = null
                })
            .ConfigureAwait(false);
        var nullCatchUp = await fixture.Executor.CatchUpOnceAsync(host).ConfigureAwait(false);
        var nullRow = await ReadRowAsync(fixture, projector, forecastId).ConfigureAwait(false);
        var nullEntry = await ReadRegistryEntryAsync(fixture, projector).ConfigureAwait(false);

        Assert.Equal(1, nullCatchUp.AppliedEvents);
        Assert.Null(nullRow.Summary);
        Assert.Equal(2, nullEntry.AppliedEventVersion);
        AssertCheckpointAdvanced(nullEntry);

        await executor.ExecuteAsync(
                new UpdateWeatherForecast
                {
                    ForecastId = forecastId,
                    Location = "Osaka",
                    Date = forecastDate.AddDays(2),
                    TemperatureC = 22,
                    Summary = "later"
                })
            .ConfigureAwait(false);
        var laterCatchUp = await fixture.Executor.CatchUpOnceAsync(host).ConfigureAwait(false);
        var laterRow = await ReadRowAsync(fixture, projector, forecastId).ConfigureAwait(false);
        var laterEntry = await ReadRegistryEntryAsync(fixture, projector).ConfigureAwait(false);

        Assert.Equal(1, laterCatchUp.AppliedEvents);
        Assert.Equal("later", laterRow.Summary);
        Assert.Equal(3, laterEntry.AppliedEventVersion);
        AssertCheckpointAdvanced(laterEntry);

        Assert.Equal(9, projector.QueryObservations.Count);
        Assert.Equal(3, projector.QueryObservations.Count(observation => observation.Port == "rows"));
        Assert.Equal(3, projector.QueryObservations.Count(observation => observation.Port == "single"));
        Assert.Equal(3, projector.QueryObservations.Count(observation => observation.Port == "scalar"));
        Assert.Contains(
            projector.QueryObservations,
            observation => observation.Input == "<null>" && observation.Output == "query-fallback");

        output.WriteLine($"apply-command={projector.LastApplySql}");
        foreach (var observation in projector.QueryObservations)
        {
            output.WriteLine(
                $"query-port={observation.Port}; input={observation.Input}; output={observation.Output}");
        }

        foreach (var characterization in projector.Characterizations)
        {
            output.WriteLine(
                $"characterization={characterization.Name}; command={characterization.Command}; outcome={characterization.Outcome}");
        }
    }

    public static async Task AssertPostgresNonInferableScalarAsync(
        PostgresMvFixture fixture,
        ITestOutputHelper output)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "PostgreSQL fixture is unavailable.");

        await fixture.ResetAsync().ConfigureAwait(false);
        var projector = new NullableTextMvProjector(runNonInferableScalar: true);
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
        await fixture.Executor.InitializeAsync(host).ConfigureAwait(false);

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        await executor.ExecuteAsync(
                new CreateWeatherForecast
                {
                    ForecastId = Guid.CreateVersion7(),
                    Location = "Tokyo",
                    Date = DateOnly.FromDateTime(DateTime.UtcNow.Date),
                    TemperatureC = 20,
                    Summary = "probe"
                })
            .ConfigureAwait(false);

        const string sql = "SELECT @P;";
        try
        {
            output.WriteLine($"provider=Postgres; version={await ReadProviderVersionAsync(fixture).ConfigureAwait(false)}");
            var catchUp = await fixture.Executor.CatchUpOnceAsync(host).ConfigureAwait(false);
            output.WriteLine($"non-inferable-command={sql}; outcome=succeeded; result={(projector.NonInferableScalarResult ?? "<null>")}");
            Assert.Equal(1, catchUp.AppliedEvents);
            Assert.Null(projector.NonInferableScalarResult);
        }
        catch (PostgresException exception)
        {
            output.WriteLine(
                $"provider=Postgres; version={await ReadProviderVersionAsync(fixture).ConfigureAwait(false)}");
            output.WriteLine(
                $"non-inferable-command={sql}; outcome=provider-limited; sql-state={exception.SqlState}; message={exception.MessageText}");
            Assert.Equal("42P18", exception.SqlState);
        }
    }

    private static void AssertCheckpointAdvanced(MvRegistryEntry entry)
    {
        Assert.True(entry.CurrentCheckpointTruth.IsKnown);
        Assert.False(string.IsNullOrWhiteSpace(entry.CurrentCheckpointTruth.PositionValue));
        Assert.False(string.IsNullOrWhiteSpace(entry.CurrentPosition));
    }

    private static async Task<NullableTextRow> ReadRowAsync(
        MultiProviderFixtureBase fixture,
        NullableTextMvProjector projector,
        Guid forecastId)
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        return await connection.QuerySingleAsync<NullableTextRow>(
                $"""
                 SELECT forecast_id AS ForecastId,
                        summary AS Summary,
                        _last_sortable_unique_id AS LastSortableUniqueId
                 FROM {projector.Rows.PhysicalName}
                 WHERE forecast_id = @ForecastId;
                 """,
                new { ForecastId = forecastId.ToString("D") })
            .ConfigureAwait(false);
    }

    private static async Task<MvRegistryEntry> ReadRegistryEntryAsync(
        MultiProviderFixtureBase fixture,
        NullableTextMvProjector projector)
    {
        var entries = await fixture.Services.GetRequiredService<IMvRegistryStore>()
            .GetEntriesAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName,
                projector.ViewVersion)
            .ConfigureAwait(false);
        return Assert.Single(entries);
    }

    private static async Task<string> ReadProviderVersionAsync(MultiProviderFixtureBase fixture)
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        var sql = fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => "SELECT version();",
            MvDbType.SqlServer => "SELECT @@VERSION;",
            MvDbType.MySql => "SELECT VERSION();",
            MvDbType.Sqlite => "SELECT sqlite_version();",
            _ => throw new NotSupportedException($"Unsupported database type '{fixture.DatabaseTypeForTests}'.")
        };
        return await connection.ExecuteScalarAsync<string>(sql).ConfigureAwait(false) ?? "<null>";
    }
}

internal sealed class NullableTextMvProjector : IMaterializedViewProjector, IMvSchemaRequirementsProvider
{
    public const string InferableTextQuery = "SELECT COALESCE(@Summary, 'query-fallback') AS summary;";

    private readonly bool _runNonInferableScalar;
    private readonly List<NullParameterQueryObservation> _queryObservations = [];
    private readonly List<NullParameterCharacterization> _characterizations = [];
    private bool _characterizationComplete;

    public NullableTextMvProjector(bool runNonInferableScalar = false)
    {
        _runNonInferableScalar = runNonInferableScalar;
    }

    public string ViewName => "NullBinding";
    public int ViewVersion => 1;
    public MvTable Rows { get; private set; } = default!;
    public string LastApplySql { get; private set; } = string.Empty;
    public string? NonInferableScalarResult { get; private set; }
    public IReadOnlyList<NullParameterQueryObservation> QueryObservations => _queryObservations;
    public IReadOnlyList<NullParameterCharacterization> Characterizations => _characterizations;

    public IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(
        MvDbType databaseType,
        IMvTableBindings tables) =>
    [
        new MvSchemaTableRequirement(
            "rows",
            tables.GetPhysicalName("rows"),
            [
                new("forecast_id", MvSchemaTypeFamily.String, false),
                new("summary", MvSchemaTypeFamily.String, true),
                new("_last_sortable_unique_id", MvSchemaTypeFamily.String, false)
            ],
            ["forecast_id"])
    ];

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        Rows = ctx.RegisterTable("rows");
        await ctx.ExecuteAsync(
                CreateTableSql(ctx.DatabaseType, Rows.PhysicalName),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default)
    {
        Guid forecastId;
        string? summary;
        switch (ev.Payload)
        {
            case WeatherForecastCreated created:
                forecastId = created.ForecastId;
                summary = created.Summary;
                break;
            case WeatherForecastUpdated updated:
                forecastId = updated.ForecastId;
                summary = updated.Summary;
                break;
            default:
                return [];
        }

        if (_runNonInferableScalar)
        {
            NonInferableScalarResult = await ctx.ExecuteScalarAsync<string?>(
                    "SELECT @P;",
                    new { P = (string?)null },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await ExerciseInferableQueryPortsAsync(ctx, summary, cancellationToken).ConfigureAwait(false);
        if (!_characterizationComplete)
        {
            await CharacterizeProviderSpecificNullsAsync(ctx, cancellationToken).ConfigureAwait(false);
            _characterizationComplete = true;
        }

        var sql = BuildUpsert(ctx.DatabaseType);
        LastApplySql = sql;
        return
        [
            new MvSqlStatement(
                sql,
                new
                {
                    ForecastId = forecastId.ToString("D"),
                    Summary = summary,
                    SortableUniqueId = ctx.CurrentSortableUniqueId
                })
        ];
    }

    private async Task ExerciseInferableQueryPortsAsync(
        IMvApplyContext ctx,
        string? summary,
        CancellationToken cancellationToken)
    {
        var parameters = new { Summary = summary };
        var expected = summary ?? "query-fallback";
        var input = summary ?? "<null>";

        var rows = await ctx.QueryRowsAsync(InferableTextQuery, parameters, cancellationToken).ConfigureAwait(false);
        if (rows.Count != 1)
        {
            throw new InvalidOperationException($"Expected one inferable query row, got {rows.Count}.");
        }

        var rowsValue = rows[0].GetString("summary");
        if (!string.Equals(rowsValue, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Inferable query rows returned '{rowsValue}', expected '{expected}'.");
        }

        _queryObservations.Add(new NullParameterQueryObservation("rows", input, rowsValue));

        var single = await ctx.QuerySingleOrDefaultRowAsync(InferableTextQuery, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (single is null)
        {
            throw new InvalidOperationException("Inferable single-row query returned no row.");
        }

        var singleValue = single.GetString("summary");
        if (!string.Equals(singleValue, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Inferable single-row query returned '{singleValue}', expected '{expected}'.");
        }

        _queryObservations.Add(new NullParameterQueryObservation("single", input, singleValue));

        var scalar = await ctx.ExecuteScalarAsync<string>(InferableTextQuery, parameters, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(scalar, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Inferable scalar query returned '{scalar}', expected '{expected}'.");
        }

        _queryObservations.Add(new NullParameterQueryObservation("scalar", input, scalar));
    }

    private async Task CharacterizeProviderSpecificNullsAsync(
        IMvApplyContext ctx,
        CancellationToken cancellationToken)
    {
        switch (ctx.DatabaseType)
        {
            case MvDbType.Postgres:
                await CharacterizeAsync(
                        ctx,
                        "postgres-uuid-timestamptz-bytea",
                        "SELECT CAST(@UuidValue AS uuid) AS uuid_value, CAST(@TimestampValue AS timestamptz) AS timestamp_value, CAST(@BytesValue AS bytea) AS bytes_value;",
                        new
                        {
                            UuidValue = (Guid?)null,
                            TimestampValue = (DateTimeOffset?)null,
                            BytesValue = (byte[]?)null
                        },
                        ["uuid_value", "timestamp_value", "bytes_value"],
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case MvDbType.SqlServer:
                await CharacterizeAsync(
                        ctx,
                        "sqlserver-int-varbinary",
                        "SELECT CAST(@IntValue AS int) AS int_value, CAST(@BytesValue AS varbinary(max)) AS bytes_value;",
                        new
                        {
                            IntValue = (int?)null,
                            BytesValue = (byte[]?)null
                        },
                        ["int_value", "bytes_value"],
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case MvDbType.MySql:
            case MvDbType.Sqlite:
                _characterizations.Add(
                    new NullParameterCharacterization(
                        $"{ctx.DatabaseType.ToString().ToLowerInvariant()}-nullable-text",
                        InferableTextQuery,
                        "supported: inferable nullable text returned the fallback value through all three query ports"));
                break;
            default:
                throw new NotSupportedException($"Unsupported database type '{ctx.DatabaseType}'.");
        }
    }

    private async Task CharacterizeAsync(
        IMvApplyContext ctx,
        string name,
        string sql,
        object parameters,
        IReadOnlyList<string> columns,
        CancellationToken cancellationToken)
    {
        try
        {
            var row = await ctx.QuerySingleOrDefaultRowAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                throw new InvalidOperationException("Provider returned no characterization row.");
            }

            if (columns.Any(column => !row.IsNull(column)))
            {
                throw new InvalidOperationException("Provider returned a non-null value for a null cast.");
            }

            _characterizations.Add(new NullParameterCharacterization(name, sql, "supported: all selected values were SQL NULL"));
        }
        catch (DbException exception)
        {
            _characterizations.Add(
                new NullParameterCharacterization(
                    name,
                    sql,
                    $"provider-limited: {exception.GetType().Name}; message={exception.Message}"));
        }
    }

    private string BuildUpsert(MvDbType databaseType) => databaseType switch
    {
        MvDbType.Postgres => $"""
            INSERT INTO {Rows.PhysicalName} (forecast_id, summary, _last_sortable_unique_id)
            VALUES (@ForecastId, @Summary, @SortableUniqueId)
            ON CONFLICT (forecast_id) DO UPDATE SET
                summary = EXCLUDED.summary,
                _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id
            WHERE {Rows.PhysicalName}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
            """,
        MvDbType.SqlServer => $"""
            MERGE {Rows.PhysicalName} AS target
            USING (
                SELECT
                    @ForecastId AS forecast_id,
                    @Summary AS summary,
                    @SortableUniqueId AS _last_sortable_unique_id
            ) AS source
            ON target.forecast_id = source.forecast_id
            WHEN MATCHED AND target._last_sortable_unique_id < source._last_sortable_unique_id THEN
                UPDATE SET
                    summary = source.summary,
                    _last_sortable_unique_id = source._last_sortable_unique_id
            WHEN NOT MATCHED THEN
                INSERT (forecast_id, summary, _last_sortable_unique_id)
                VALUES (source.forecast_id, source.summary, source._last_sortable_unique_id);
            """,
        MvDbType.MySql => $"""
            INSERT INTO {Rows.PhysicalName} (forecast_id, summary, _last_sortable_unique_id)
            VALUES (@ForecastId, @Summary, @SortableUniqueId)
            ON DUPLICATE KEY UPDATE
                summary = IF(_last_sortable_unique_id < VALUES(_last_sortable_unique_id), VALUES(summary), summary),
                _last_sortable_unique_id = IF(_last_sortable_unique_id < VALUES(_last_sortable_unique_id), VALUES(_last_sortable_unique_id), _last_sortable_unique_id);
            """,
        MvDbType.Sqlite => $"""
            INSERT INTO {Rows.PhysicalName} (forecast_id, summary, _last_sortable_unique_id)
            VALUES (@ForecastId, @Summary, @SortableUniqueId)
            ON CONFLICT (forecast_id) DO UPDATE SET
                summary = excluded.summary,
                _last_sortable_unique_id = excluded._last_sortable_unique_id
            WHERE {Rows.PhysicalName}._last_sortable_unique_id < excluded._last_sortable_unique_id;
            """,
        _ => throw new NotSupportedException($"Unsupported database type '{databaseType}'.")
    };

    private static string CreateTableSql(MvDbType databaseType, string tableName) => databaseType switch
    {
        MvDbType.Postgres => $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                forecast_id VARCHAR(36) NOT NULL PRIMARY KEY,
                summary TEXT NULL,
                _last_sortable_unique_id VARCHAR(64) NOT NULL
            );
            """,
        MvDbType.SqlServer => $"""
            IF OBJECT_ID(N'{tableName}', N'U') IS NULL
            BEGIN
                CREATE TABLE {tableName} (
                    forecast_id NVARCHAR(36) NOT NULL PRIMARY KEY,
                    summary NVARCHAR(MAX) NULL,
                    _last_sortable_unique_id NVARCHAR(64) NOT NULL
                );
            END;
            """,
        MvDbType.MySql => $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                forecast_id VARCHAR(36) NOT NULL PRIMARY KEY,
                summary TEXT NULL,
                _last_sortable_unique_id VARCHAR(64) NOT NULL
            );
            """,
        MvDbType.Sqlite => $"""
            CREATE TABLE IF NOT EXISTS {tableName} (
                forecast_id TEXT NOT NULL PRIMARY KEY,
                summary TEXT NULL,
                _last_sortable_unique_id TEXT NOT NULL
            );
            """,
        _ => throw new NotSupportedException($"Unsupported database type '{databaseType}'.")
    };
}

internal sealed class NullableTextRow
{
    public string ForecastId { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string LastSortableUniqueId { get; init; } = string.Empty;
}

internal sealed record NullParameterQueryObservation(string Port, string Input, string Output);

internal sealed record NullParameterCharacterization(string Name, string Command, string Outcome);
