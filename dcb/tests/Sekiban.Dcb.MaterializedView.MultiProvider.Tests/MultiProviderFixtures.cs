using System.Diagnostics.CodeAnalysis;
using System.Data.Common;
using Dapper;
using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Weather;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

public sealed class CrossProviderWeatherForecastMvV1 : IMaterializedViewProjector, IMvSchemaRequirementsProvider
{
    public string ViewName => "WeatherForecastPortable";
    public int ViewVersion => 1;

    public MvTable Forecasts { get; private set; } = default!;

    public IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(
        MvDbType databaseType,
        IMvTableBindings tables) =>
    [
        new MvSchemaTableRequirement(
            "forecasts",
            tables.GetPhysicalName("forecasts"),
            [
                new("forecast_id", MvSchemaTypeFamily.String, false),
                new("location", MvSchemaTypeFamily.String, false),
                new("forecast_date", MvSchemaTypeFamily.DateTime, false),
                new("temperature_c", MvSchemaTypeFamily.Integer, false),
                new("summary", MvSchemaTypeFamily.String, true),
                new("is_deleted", MvSchemaTypeFamily.Boolean, false),
                new("_last_sortable_unique_id", MvSchemaTypeFamily.String, false),
                new("_last_applied_at", MvSchemaTypeFamily.DateTime, false)
            ],
            ["forecast_id"])
    ];

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        Forecasts = ctx.RegisterTable("forecasts");
        await ctx.ExecuteAsync(CreateTableSql(ctx.DatabaseType, Forecasts.PhysicalName), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MvSqlStatement>>(
            ev.Payload switch
            {
                WeatherForecastCreated created => [BuildUpsert(ctx.DatabaseType, created.ForecastId, created.Location, created.Date, created.TemperatureC, created.Summary, false, ctx.CurrentSortableUniqueId)],
                WeatherForecastUpdated updated => [BuildUpsert(ctx.DatabaseType, updated.ForecastId, updated.Location, updated.Date, updated.TemperatureC, updated.Summary, false, ctx.CurrentSortableUniqueId)],
                WeatherForecastDeleted deleted => [BuildDelete(ctx.DatabaseType, deleted.ForecastId, ctx.CurrentSortableUniqueId)],
                _ => []
            });

    private MvSqlStatement BuildUpsert(
        MvDbType dbType,
        Guid forecastId,
        string location,
        DateOnly date,
        int temperatureC,
        string? summary,
        bool isDeleted,
        string sortableUniqueId)
    {
        var parameters = new
        {
            ForecastId = forecastId.ToString("D"),
            Location = location,
            ForecastDate = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            TemperatureC = temperatureC,
            Summary = summary,
            IsDeleted = isDeleted,
            SortableUniqueId = sortableUniqueId
        };

        return new MvSqlStatement(dbType switch
        {
            MvDbType.Postgres => $"""
                INSERT INTO {Forecasts.PhysicalName}
                    (forecast_id, location, forecast_date, temperature_c, summary, is_deleted, _last_sortable_unique_id, _last_applied_at)
                VALUES
                    (@ForecastId, @Location, @ForecastDate, @TemperatureC, @Summary, @IsDeleted, @SortableUniqueId, CURRENT_TIMESTAMP)
                ON CONFLICT (forecast_id) DO UPDATE SET
                    location = EXCLUDED.location,
                    forecast_date = EXCLUDED.forecast_date,
                    temperature_c = EXCLUDED.temperature_c,
                    summary = EXCLUDED.summary,
                    is_deleted = EXCLUDED.is_deleted,
                    _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                    _last_applied_at = CURRENT_TIMESTAMP
                WHERE {Forecasts.PhysicalName}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
                """,
            MvDbType.SqlServer => $"""
                MERGE {Forecasts.PhysicalName} AS target
                USING (
                    SELECT
                        @ForecastId AS forecast_id,
                        @Location AS location,
                        @ForecastDate AS forecast_date,
                        @TemperatureC AS temperature_c,
                        @Summary AS summary,
                        @IsDeleted AS is_deleted,
                        @SortableUniqueId AS _last_sortable_unique_id
                ) AS source
                ON target.forecast_id = source.forecast_id
                WHEN MATCHED AND target._last_sortable_unique_id < source._last_sortable_unique_id THEN
                    UPDATE SET
                        location = source.location,
                        forecast_date = source.forecast_date,
                        temperature_c = source.temperature_c,
                        summary = source.summary,
                        is_deleted = source.is_deleted,
                        _last_sortable_unique_id = source._last_sortable_unique_id,
                        _last_applied_at = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (forecast_id, location, forecast_date, temperature_c, summary, is_deleted, _last_sortable_unique_id, _last_applied_at)
                    VALUES (source.forecast_id, source.location, source.forecast_date, source.temperature_c, source.summary, source.is_deleted, source._last_sortable_unique_id, SYSUTCDATETIME());
                """,
            MvDbType.MySql => $"""
                INSERT INTO {Forecasts.PhysicalName}
                    (forecast_id, location, forecast_date, temperature_c, summary, is_deleted, _last_sortable_unique_id, _last_applied_at)
                VALUES
                    (@ForecastId, @Location, @ForecastDate, @TemperatureC, @Summary, @IsDeleted, @SortableUniqueId, CURRENT_TIMESTAMP(6))
                ON DUPLICATE KEY UPDATE
                    location = IF(_last_sortable_unique_id < VALUES(_last_sortable_unique_id), VALUES(location), location),
                    forecast_date = IF(_last_sortable_unique_id < VALUES(_last_sortable_unique_id), VALUES(forecast_date), forecast_date),
                    temperature_c = IF(_last_sortable_unique_id < VALUES(_last_sortable_unique_id), VALUES(temperature_c), temperature_c),
                    summary = IF(_last_sortable_unique_id < VALUES(_last_sortable_unique_id), VALUES(summary), summary),
                    is_deleted = IF(_last_sortable_unique_id < VALUES(_last_sortable_unique_id), VALUES(is_deleted), is_deleted),
                    _last_sortable_unique_id = IF(_last_sortable_unique_id < VALUES(_last_sortable_unique_id), VALUES(_last_sortable_unique_id), _last_sortable_unique_id),
                    _last_applied_at = IF(_last_sortable_unique_id < VALUES(_last_sortable_unique_id), CURRENT_TIMESTAMP(6), _last_applied_at);
                """,
            MvDbType.Sqlite => $"""
                INSERT INTO {Forecasts.PhysicalName}
                    (forecast_id, location, forecast_date, temperature_c, summary, is_deleted, _last_sortable_unique_id, _last_applied_at)
                VALUES
                    (@ForecastId, @Location, @ForecastDate, @TemperatureC, @Summary, @IsDeleted, @SortableUniqueId, CURRENT_TIMESTAMP)
                ON CONFLICT (forecast_id) DO UPDATE SET
                    location = excluded.location,
                    forecast_date = excluded.forecast_date,
                    temperature_c = excluded.temperature_c,
                    summary = excluded.summary,
                    is_deleted = excluded.is_deleted,
                    _last_sortable_unique_id = excluded._last_sortable_unique_id,
                    _last_applied_at = CURRENT_TIMESTAMP
                WHERE {Forecasts.PhysicalName}._last_sortable_unique_id < excluded._last_sortable_unique_id;
                """,
            _ => throw new NotSupportedException($"Database type '{dbType}' is not supported.")
        }, parameters);
    }

    private static string CreateTableSql(MvDbType dbType, string tableName) =>
        dbType switch
        {
            MvDbType.Postgres => $"""
                CREATE TABLE IF NOT EXISTS {tableName} (
                    forecast_id VARCHAR(36) NOT NULL PRIMARY KEY,
                    location VARCHAR(200) NOT NULL,
                    forecast_date TIMESTAMPTZ NOT NULL,
                    temperature_c INTEGER NOT NULL,
                    summary TEXT NULL,
                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                    _last_sortable_unique_id VARCHAR(64) NOT NULL,
                    _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """,
            MvDbType.SqlServer => $"""
                IF OBJECT_ID(N'{tableName}', N'U') IS NULL
                BEGIN
                    CREATE TABLE {tableName} (
                        forecast_id NVARCHAR(36) NOT NULL PRIMARY KEY,
                        location NVARCHAR(200) NOT NULL,
                        forecast_date DATETIME2 NOT NULL,
                        temperature_c INT NOT NULL,
                        summary NVARCHAR(MAX) NULL,
                        is_deleted BIT NOT NULL CONSTRAINT DF_{tableName}_is_deleted DEFAULT 0,
                        _last_sortable_unique_id NVARCHAR(64) NOT NULL,
                        _last_applied_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_{tableName}_last_applied_at DEFAULT SYSUTCDATETIME()
                    );
                END;
                """,
            MvDbType.MySql => $"""
                CREATE TABLE IF NOT EXISTS {tableName} (
                    forecast_id VARCHAR(36) NOT NULL PRIMARY KEY,
                    location VARCHAR(200) NOT NULL,
                    forecast_date DATETIME(6) NOT NULL,
                    temperature_c INT NOT NULL,
                    summary TEXT NULL,
                    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
                    _last_sortable_unique_id VARCHAR(64) NOT NULL,
                    _last_applied_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)
                );
                """,
            MvDbType.Sqlite => $"""
                CREATE TABLE IF NOT EXISTS {tableName} (
                    forecast_id TEXT NOT NULL PRIMARY KEY,
                    location TEXT NOT NULL,
                    forecast_date TEXT NOT NULL,
                    temperature_c INTEGER NOT NULL,
                    summary TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    _last_sortable_unique_id TEXT NOT NULL,
                    _last_applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """,
            _ => throw new NotSupportedException($"Database type '{dbType}' is not supported.")
        };

    private MvSqlStatement BuildDelete(MvDbType dbType, Guid forecastId, string sortableUniqueId) =>
        new(dbType switch
        {
            MvDbType.Postgres => $"""
                UPDATE {Forecasts.PhysicalName}
                SET is_deleted = TRUE,
                    _last_sortable_unique_id = @SortableUniqueId,
                    _last_applied_at = CURRENT_TIMESTAMP
                WHERE forecast_id = @ForecastId
                  AND _last_sortable_unique_id < @SortableUniqueId;
                """,
            MvDbType.SqlServer => $"""
                UPDATE {Forecasts.PhysicalName}
                SET is_deleted = 1,
                    _last_sortable_unique_id = @SortableUniqueId,
                    _last_applied_at = SYSUTCDATETIME()
                WHERE forecast_id = @ForecastId
                  AND _last_sortable_unique_id < @SortableUniqueId;
                """,
            MvDbType.MySql => $"""
                UPDATE {Forecasts.PhysicalName}
                SET is_deleted = TRUE,
                    _last_sortable_unique_id = @SortableUniqueId,
                    _last_applied_at = CURRENT_TIMESTAMP(6)
                WHERE forecast_id = @ForecastId
                  AND _last_sortable_unique_id < @SortableUniqueId;
                """,
            MvDbType.Sqlite => $"""
                UPDATE {Forecasts.PhysicalName}
                SET is_deleted = 1,
                    _last_sortable_unique_id = @SortableUniqueId,
                    _last_applied_at = CURRENT_TIMESTAMP
                WHERE forecast_id = @ForecastId
                  AND _last_sortable_unique_id < @SortableUniqueId;
                """,
            _ => throw new NotSupportedException($"Database type '{dbType}' is not supported.")
        }, new
        {
            ForecastId = forecastId.ToString("D"),
            SortableUniqueId = sortableUniqueId
        });
}

public sealed class ForecastDbRow
{
    public string ForecastId { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public bool IsDeleted { get; init; }
    public string LastSortableUniqueId { get; init; } = string.Empty;
}

public sealed class RegistryDbRow
{
    public long AppliedEventVersion { get; init; }
    public string CurrentPosition { get; init; } = string.Empty;
    public string LastAppliedSource { get; init; } = string.Empty;
}

public abstract class MultiProviderFixtureBase : IAsyncLifetime
{
    internal const string ForecastTable = "sekiban_mv_weatherforecastportable_v1_forecasts";
    internal const string ServiceId = "multi-provider-service";

    private ServiceProvider? _services;
    private string? _connectionString;
    private string? _skipReason;

    public ServiceProvider Services => _services ?? throw new InvalidOperationException("Fixture not initialized.");
    public IMvExecutor Executor => Services.GetRequiredService<IMvExecutor>();
    public IEventStoreFactory EventStoreFactory => Services.GetRequiredService<IEventStoreFactory>();
    public InMemoryEventStore EventStore => Services.GetRequiredService<InMemoryEventStore>();
    public InMemoryObjectAccessor ActorAccessor => Services.GetRequiredService<InMemoryObjectAccessor>();
    public DcbDomainTypes DomainTypes => Services.GetRequiredService<DcbDomainTypes>();
    public bool IsAvailable => _skipReason is null;
    public string? AvailabilityMessage => _skipReason;
    protected string ConnectionString => _connectionString ?? throw new InvalidOperationException("Fixture not initialized.");

    // Public alias exposed to sibling test files that need the raw connection
    // string (the unsafe-window MV harnesses wire their own initializer /
    // catch-up / promoter rather than going through IMvExecutor).
    public string ConnectionStringForTests => ConnectionString;
    public virtual string? InspectionConnectionStringForTests => null;

    protected abstract MvDbType DatabaseType { get; }
    internal MvDbType DatabaseTypeForTests => DatabaseType;
    protected abstract Task<string> CreateConnectionStringAsync();
    protected abstract void RegisterProvider(IServiceCollection services, string connectionString);
    protected abstract DbConnection CreateConnection(string connectionString);
    protected abstract string ResetSql { get; }
    protected abstract string LegacyRegistrySql { get; }
    public abstract string CreateCandidateMismatchTriggerSql { get; }
    public abstract string DropCandidateMismatchTriggerSql { get; }
    public virtual string CandidateTruthUpdateSql => """
        UPDATE sekiban_mv_registry
        SET target_checkpoint_truth = @TargetTruth
        WHERE service_id = @ServiceId
          AND view_name = @ViewName
          AND view_version = @ViewVersion;
        """;

    public async Task InitializeAsync()
    {
        try
        {
            _connectionString = await CreateConnectionStringAsync().ConfigureAwait(false);
        }
        catch (DockerUnavailableException ex)
        {
            _skipReason = $"{DatabaseType} integration tests require Docker: {ex.Message}";
            return;
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(DomainType.GetDomainTypes());
        services.AddSingleton<InMemoryEventStore>(sp =>
            new InMemoryEventStore(
                sp.GetRequiredService<DcbDomainTypes>().EventTypes,
                new FixedServiceIdProvider(ServiceId)));
        services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<InMemoryEventStore>());
        services.AddSingleton<IEventStoreFactory>(sp =>
            new Sekiban.Dcb.InMemory.InMemoryEventStoreFactory(sp.GetRequiredService<InMemoryEventStore>()));
        services.AddSingleton<InMemoryObjectAccessor>(sp =>
            new InMemoryObjectAccessor(sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<DcbDomainTypes>()));
        services.AddMaterializedView<CrossProviderWeatherForecastMvV1>();
        RegisterProvider(services, _connectionString);
        _services = services.BuildServiceProvider();

        await ResetAsync().ConfigureAwait(false);
    }

    public virtual Task DisposeAsync()
    {
        _services?.Dispose();
        return Task.CompletedTask;
    }

    public async Task<DbConnection> OpenConnectionAsync()
    {
        var connection = CreateConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        return connection;
    }

    public async Task ResetAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        EventStore.Clear();
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await connection.ExecuteAsync(ResetSql).ConfigureAwait(false);
    }

    public async Task PrepareLegacyRegistryAsync()
    {
        EnsureAvailable();
        await ResetAsync().ConfigureAwait(false);
        await using var connection = await OpenConnectionAsync().ConfigureAwait(false);
        await connection.ExecuteAsync(LegacyRegistrySql).ConfigureAwait(false);
        await connection.ExecuteAsync(
                """
                INSERT INTO sekiban_mv_registry (
                    service_id, view_name, view_version, logical_table, physical_table, status, last_updated)
                VALUES ('legacy-service', 'Legacy', 1, 'orders', 'legacy_orders', 'ready', @LastUpdated);
                """,
                new { LastUpdated = DateTimeOffset.UtcNow })
            .ConfigureAwait(false);
    }

    public void EnsureAvailable()
    {
        if (_skipReason is not null)
        {
            ThrowUnavailable();
        }
    }

    [DoesNotReturn]
    private void ThrowUnavailable() => throw new InvalidOperationException(_skipReason);
}

public sealed class PostgresMvFixture : MultiProviderFixtureBase
{
    private PostgreSqlContainer? _container;

    protected override MvDbType DatabaseType => MvDbType.Postgres;

    protected override async Task<string> CreateConnectionStringAsync()
    {
        _container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("sekiban_mv_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
        return _container.GetConnectionString();
    }

    protected override void RegisterProvider(IServiceCollection services, string connectionString)
    {
        services.AddSekibanDcbMaterializedView(options =>
        {
            options.ServiceId = ServiceId;
            options.BatchSize = 100;
            options.SafeWindowMs = 0;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
        });
        services.AddSekibanDcbMaterializedViewPostgres(connectionString, registerHostedWorker: false);
    }

    protected override DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(connectionString);

    protected override string ResetSql => """
        DROP TABLE IF EXISTS sekiban_mv_weatherforecastportable_v1_forecasts;
        DROP TABLE IF EXISTS sekiban_mv_active;
        DROP TABLE IF EXISTS sekiban_mv_registry;
        """;

    protected override string LegacyRegistrySql => """
        CREATE TABLE sekiban_mv_registry (
            service_id TEXT NOT NULL,
            view_name TEXT NOT NULL,
            view_version INT NOT NULL,
            logical_table TEXT NOT NULL,
            physical_table TEXT NOT NULL,
            status TEXT NOT NULL,
            current_position TEXT NULL,
            target_position TEXT NULL,
            last_sortable_unique_id TEXT NULL,
            applied_event_version BIGINT NOT NULL DEFAULT 0,
            last_applied_source TEXT NULL,
            last_applied_at TIMESTAMPTZ NULL,
            last_stream_received_sortable_unique_id TEXT NULL,
            last_stream_received_at TIMESTAMPTZ NULL,
            last_stream_applied_sortable_unique_id TEXT NULL,
            last_catch_up_sortable_unique_id TEXT NULL,
            last_updated TIMESTAMPTZ NOT NULL,
            metadata JSONB NULL,
            PRIMARY KEY (service_id, view_name, view_version, logical_table)
        );
        """;

    public override string CandidateTruthUpdateSql => """
        UPDATE sekiban_mv_registry
        SET target_checkpoint_truth = CAST(@TargetTruth AS jsonb)
        WHERE service_id = @ServiceId
          AND view_name = @ViewName
          AND view_version = @ViewVersion;
        """;

    public override string CreateCandidateMismatchTriggerSql => """
        CREATE OR REPLACE FUNCTION sekiban_mv_force_candidate_mismatch() RETURNS trigger AS $$
        BEGIN
            UPDATE sekiban_mv_registry
            SET status = 'catchingup'
            WHERE service_id = NEW.service_id
              AND view_name = NEW.view_name
              AND view_version = NEW.active_version;
            RETURN NEW;
        END;
        $$ LANGUAGE plpgsql;
        CREATE TRIGGER sekiban_mv_force_candidate_mismatch
        AFTER INSERT ON sekiban_mv_active
        FOR EACH ROW EXECUTE FUNCTION sekiban_mv_force_candidate_mismatch();
        """;

    public override string DropCandidateMismatchTriggerSql => """
        DROP TRIGGER IF EXISTS sekiban_mv_force_candidate_mismatch ON sekiban_mv_active;
        DROP FUNCTION IF EXISTS sekiban_mv_force_candidate_mismatch();
        """;

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class MySqlMvFixture : MultiProviderFixtureBase
{
    private MySqlContainer? _container;

    protected override MvDbType DatabaseType => MvDbType.MySql;

    protected override async Task<string> CreateConnectionStringAsync()
    {
        _container = new MySqlBuilder("mysql:8.4")
            .WithDatabase("sekiban_mv_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .WithCommand("--log-bin-trust-function-creators=1")
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
        return _container.GetConnectionString();
    }

    protected override void RegisterProvider(IServiceCollection services, string connectionString)
    {
        services.AddSekibanDcbMaterializedView(options =>
        {
            options.ServiceId = ServiceId;
            options.BatchSize = 100;
            options.SafeWindowMs = 0;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
        });
        services.AddSekibanDcbMaterializedViewMySql(connectionString, registerHostedWorker: false);
    }

    protected override DbConnection CreateConnection(string connectionString) => new MySqlConnection(connectionString);

    protected override string ResetSql => """
        DROP TABLE IF EXISTS sekiban_mv_weatherforecastportable_v1_forecasts;
        DROP TABLE IF EXISTS sekiban_mv_active;
        DROP TABLE IF EXISTS sekiban_mv_registry;
        """;

    protected override string LegacyRegistrySql => """
        CREATE TABLE sekiban_mv_registry (
            service_id VARCHAR(255) NOT NULL,
            view_name VARCHAR(255) NOT NULL,
            view_version INT NOT NULL,
            logical_table VARCHAR(255) NOT NULL,
            physical_table VARCHAR(255) NOT NULL,
            status VARCHAR(32) NOT NULL,
            current_position VARCHAR(64) NULL,
            target_position VARCHAR(64) NULL,
            last_sortable_unique_id VARCHAR(64) NULL,
            applied_event_version BIGINT NOT NULL DEFAULT 0,
            last_applied_source VARCHAR(32) NULL,
            last_applied_at DATETIME(6) NULL,
            last_stream_received_sortable_unique_id VARCHAR(64) NULL,
            last_stream_received_at DATETIME(6) NULL,
            last_stream_applied_sortable_unique_id VARCHAR(64) NULL,
            last_catch_up_sortable_unique_id VARCHAR(64) NULL,
            last_updated DATETIME(6) NOT NULL,
            metadata LONGTEXT NULL,
            PRIMARY KEY (service_id, view_name, view_version, logical_table)
        );
        """;

    public override string CreateCandidateMismatchTriggerSql => """
        CREATE TRIGGER sekiban_mv_force_candidate_mismatch
        AFTER INSERT ON sekiban_mv_active
        FOR EACH ROW
        UPDATE sekiban_mv_registry
        SET status = 'catchingup'
        WHERE service_id = NEW.service_id
          AND view_name = NEW.view_name
          AND view_version = NEW.active_version;
        """;

    public override string DropCandidateMismatchTriggerSql =>
        "DROP TRIGGER IF EXISTS sekiban_mv_force_candidate_mismatch;";

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class SqlServerMvFixture : MultiProviderFixtureBase
{
    private MsSqlContainer? _container;
    private string? _inspectionLogin;
    private string? _inspectionPassword;
    private string? _inspectionConnectionString;

    protected override MvDbType DatabaseType => MvDbType.SqlServer;

    protected override async Task<string> CreateConnectionStringAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
        var connectionString = _container.GetConnectionString();
        _inspectionLogin = $"mv_inspector_{Guid.NewGuid():N}";
        _inspectionPassword = "SekibanInspector_9x!";
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await connection.ExecuteAsync($"""
                CREATE LOGIN [{_inspectionLogin}] WITH PASSWORD = '{_inspectionPassword}';
                CREATE USER [{_inspectionLogin}] FOR LOGIN [{_inspectionLogin}];
                ALTER ROLE db_datareader ADD MEMBER [{_inspectionLogin}];
                GRANT VIEW DEFINITION TO [{_inspectionLogin}];
                """).ConfigureAwait(false);
        }

        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            UserID = _inspectionLogin,
            Password = _inspectionPassword,
            IntegratedSecurity = false,
            ApplicationIntent = ApplicationIntent.ReadWrite,
            Pooling = false
        };
        _inspectionConnectionString = builder.ConnectionString;
        return connectionString;
    }

    public override string? InspectionConnectionStringForTests => _inspectionConnectionString;

    protected override void RegisterProvider(IServiceCollection services, string connectionString)
    {
        services.AddSekibanDcbMaterializedView(options =>
        {
            options.ServiceId = ServiceId;
            options.BatchSize = 100;
            options.SafeWindowMs = 0;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
            options.SqlServerInspectionConnectionString = _inspectionConnectionString;
        });
        services.AddSekibanDcbMaterializedViewSqlServer(connectionString, registerHostedWorker: false);
    }

    protected override DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    protected override string ResetSql => """
        IF OBJECT_ID(N'sekiban_mv_weatherforecastportable_v1_forecasts', N'U') IS NOT NULL DROP TABLE sekiban_mv_weatherforecastportable_v1_forecasts;
        IF OBJECT_ID(N'sekiban_mv_active', N'U') IS NOT NULL DROP TABLE sekiban_mv_active;
        IF OBJECT_ID(N'sekiban_mv_registry', N'U') IS NOT NULL DROP TABLE sekiban_mv_registry;
        """;

    protected override string LegacyRegistrySql => """
        CREATE TABLE sekiban_mv_registry (
            service_id NVARCHAR(255) NOT NULL,
            view_name NVARCHAR(255) NOT NULL,
            view_version INT NOT NULL,
            logical_table NVARCHAR(255) NOT NULL,
            physical_table NVARCHAR(255) NOT NULL,
            status NVARCHAR(32) NOT NULL,
            current_position NVARCHAR(64) NULL,
            target_position NVARCHAR(64) NULL,
            last_sortable_unique_id NVARCHAR(64) NULL,
            applied_event_version BIGINT NOT NULL CONSTRAINT DF_legacy_registry_applied_event_version DEFAULT 0,
            last_applied_source NVARCHAR(32) NULL,
            last_applied_at DATETIMEOFFSET NULL,
            last_stream_received_sortable_unique_id NVARCHAR(64) NULL,
            last_stream_received_at DATETIMEOFFSET NULL,
            last_stream_applied_sortable_unique_id NVARCHAR(64) NULL,
            last_catch_up_sortable_unique_id NVARCHAR(64) NULL,
            last_updated DATETIMEOFFSET NOT NULL,
            metadata NVARCHAR(MAX) NULL,
            CONSTRAINT PK_legacy_sekiban_mv_registry PRIMARY KEY (service_id, view_name, view_version, logical_table)
        );
        """;

    public override string CreateCandidateMismatchTriggerSql => """
        CREATE TRIGGER sekiban_mv_force_candidate_mismatch ON sekiban_mv_active
        AFTER INSERT AS
        BEGIN
            SET NOCOUNT ON;
            UPDATE registry
            SET status = 'catchingup'
            FROM sekiban_mv_registry AS registry
            INNER JOIN inserted AS active
                ON registry.service_id = active.service_id
               AND registry.view_name = active.view_name
               AND registry.view_version = active.active_version;
        END;
        """;

    public override string DropCandidateMismatchTriggerSql => """
        IF OBJECT_ID(N'sekiban_mv_force_candidate_mismatch', N'TR') IS NOT NULL
            DROP TRIGGER sekiban_mv_force_candidate_mismatch;
        """;

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (_container is not null)
        {
            if (_inspectionLogin is not null)
            {
                await using var connection = new SqlConnection(_container.GetConnectionString());
                await connection.OpenAsync().ConfigureAwait(false);
                await connection.ExecuteAsync($"""
                    IF USER_ID(N'{_inspectionLogin}') IS NOT NULL DROP USER [{_inspectionLogin}];
                    IF SUSER_ID(N'{_inspectionLogin}') IS NOT NULL DROP LOGIN [{_inspectionLogin}];
                    """).ConfigureAwait(false);
            }

            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }
}

public sealed class SqliteMvFixture : MultiProviderFixtureBase
{
    private string? _databasePath;

    protected override MvDbType DatabaseType => MvDbType.Sqlite;

    protected override Task<string> CreateConnectionStringAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"sekiban-mv-{Guid.NewGuid():N}.db");
        return Task.FromResult($"Data Source={_databasePath}");
    }

    protected override void RegisterProvider(IServiceCollection services, string connectionString)
    {
        services.AddSekibanDcbMaterializedView(options =>
        {
            options.ServiceId = ServiceId;
            options.BatchSize = 100;
            options.SafeWindowMs = 0;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
        });
        services.AddSekibanDcbMaterializedViewSqlite(connectionString, registerHostedWorker: false);
    }

    protected override DbConnection CreateConnection(string connectionString) => new SqliteConnection(connectionString);

    protected override string ResetSql => """
        DROP TABLE IF EXISTS sekiban_mv_weatherforecastportable_v1_forecasts;
        DROP TABLE IF EXISTS sekiban_mv_policysurface_v1_records;
        DROP TABLE IF EXISTS sekiban_mv_active;
        DROP TABLE IF EXISTS sekiban_mv_registry;
        """;

    protected override string LegacyRegistrySql => """
        CREATE TABLE sekiban_mv_registry (
            service_id TEXT NOT NULL,
            view_name TEXT NOT NULL,
            view_version INTEGER NOT NULL,
            logical_table TEXT NOT NULL,
            physical_table TEXT NOT NULL,
            status TEXT NOT NULL,
            current_position TEXT NULL,
            target_position TEXT NULL,
            last_sortable_unique_id TEXT NULL,
            applied_event_version INTEGER NOT NULL DEFAULT 0,
            last_applied_source TEXT NULL,
            last_applied_at TEXT NULL,
            last_stream_received_sortable_unique_id TEXT NULL,
            last_stream_received_at TEXT NULL,
            last_stream_applied_sortable_unique_id TEXT NULL,
            last_catch_up_sortable_unique_id TEXT NULL,
            last_updated TEXT NOT NULL,
            metadata TEXT NULL,
            PRIMARY KEY (service_id, view_name, view_version, logical_table)
        );
        """;

    public override string CreateCandidateMismatchTriggerSql => """
        CREATE TRIGGER sekiban_mv_force_candidate_mismatch
        AFTER INSERT ON sekiban_mv_active
        BEGIN
            UPDATE sekiban_mv_registry
            SET status = 'catchingup'
            WHERE service_id = NEW.service_id
              AND view_name = NEW.view_name
              AND view_version = NEW.active_version;
        END;
        """;

    public override string DropCandidateMismatchTriggerSql =>
        "DROP TRIGGER IF EXISTS sekiban_mv_force_candidate_mismatch;";

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (_databasePath is not null && File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
