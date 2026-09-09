using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sekiban.Dcb;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     Real PostgreSQL proof for SEK-G35. The statement must reach its conflict/update arm for an existing row while
///     still rejecting an expected&gt;0 write when the row is absent; a simplified unconditional INSERT is not acceptable.
/// </summary>
[Collection("PostgresTests")]
public sealed class PostgresProjectionStatusStoreTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;

    public PostgresProjectionStatusStoreTests(PostgresTestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var context = await _fixture.DbContextFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS dcb_projection_statuses");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CasMatrix_UsesReachableUpdateAndConditionalCreateAgainstRealPostgres()
    {
        var store = new PostgresMultiProjectionStateStore(
            _fixture.DbContextFactory,
            new FixedServiceIdProvider("svc"));
        var first = Heartbeat("activation-a", 1);

        var created = await store.UpsertAsync(first, 0);
        var updated = await store.UpsertAsync(first with { Sequence = 2, AppliedEventCount = 2 }, 1);
        var staleCreate = await store.UpsertAsync(first with { Sequence = 3 }, 0);

        Assert.True(created.IsSuccess);
        Assert.True(created.GetValue().Committed);
        Assert.True(updated.IsSuccess);
        Assert.True(updated.GetValue().Committed);
        Assert.Equal(2, updated.GetValue().Current!.Sequence);
        Assert.Equal(2, updated.GetValue().Current!.AppliedEventCount);
        Assert.True(staleCreate.IsSuccess);
        var createRaceConflict = Assert.IsType<ProjectionStatusWriteConflict>(staleCreate.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.RowAlreadyExists, createRaceConflict.Reason);
        Assert.Equal(0, createRaceConflict.ExpectedSequence);
        Assert.Equal(2, createRaceConflict.ObservedSequence);
        Assert.Equal(first.ProjectorVersion, createRaceConflict.ExpectedProjectorVersion);
        Assert.Equal(first.ProjectorVersion, createRaceConflict.ObservedProjectorVersion);
        Assert.Equal(createRaceConflict.ToCompatibilityReason(), staleCreate.GetValue().ConflictReason);
        Assert.DoesNotContain("activation row already exists", staleCreate.GetValue().ConflictReason!, StringComparison.OrdinalIgnoreCase);

        var staleUpdate = await store.UpsertAsync(first with { Sequence = 3 }, 1);
        Assert.True(staleUpdate.IsSuccess);
        var staleUpdateConflict = Assert.IsType<ProjectionStatusWriteConflict>(staleUpdate.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.SequenceMismatch, staleUpdateConflict.Reason);
        Assert.Equal(1, staleUpdateConflict.ExpectedSequence);
        Assert.Equal(2, staleUpdateConflict.ObservedSequence);

        var missing = first with { ClusterId = "missing", Sequence = 2 };
        var absent = await store.UpsertAsync(missing, 1);
        Assert.True(absent.IsSuccess);
        var absentConflict = Assert.IsType<ProjectionStatusWriteConflict>(absent.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.RowAbsent, absentConflict.Reason);
        Assert.Equal(1, absentConflict.ExpectedSequence);
        Assert.Null(absent.GetValue().Current);
        Assert.Null(absentConflict.ObservedSequence);
        Assert.Equal(missing.ProjectorVersion, absentConflict.ExpectedProjectorVersion);
        Assert.Null(absentConflict.ObservedProjectorVersion);
        var listedAfterAbsentWrite = await store.ListAsync();
        Assert.True(
            listedAfterAbsentWrite.IsSuccess,
            listedAfterAbsentWrite.IsSuccess ? string.Empty : listedAfterAbsentWrite.GetException().ToString());
        Assert.DoesNotContain(listedAfterAbsentWrite.GetValue(), row => row.ClusterId == "missing");

        // A later conditional create can race. The loser observes RowAlreadyExists and must not overwrite the winner.
        var competitor = missing with { ActivationId = "competitor", Sequence = 1 };
        Assert.True((await store.UpsertAsync(competitor, 0)).GetValue().Committed);
        var losingCreate = await store.UpsertAsync(missing with { ActivationId = "retrying", Sequence = 1 }, 0);
        Assert.True(losingCreate.IsSuccess);
        Assert.Equal(
            ProjectionStatusConflictReason.RowAlreadyExists,
            Assert.IsType<ProjectionStatusWriteConflict>(losingCreate.GetValue().ConflictDetails).Reason);
        Assert.Equal("competitor", losingCreate.GetValue().Current!.ActivationId);

        var afterRebase = await store.UpsertAsync(missing with { ActivationId = "retrying", Sequence = 2 }, 1);
        Assert.True(afterRebase.IsSuccess);
        Assert.True(afterRebase.GetValue().Committed);
        Assert.Equal(2, afterRebase.GetValue().Current!.Sequence);
    }

    [Fact]
    public async Task PreProvisionedRuntime_UsesOnlyDml_PreservesCasAndServiceIsolation()
    {
        await _fixture.DbContextFactory.ProvisionProjectionStatusSchemaAsync();
        var runtime = await CreateDmlOnlyRuntimePrincipalAsync(includeStatusTable: true);
        try
        {
            var commands = new List<string>();
            var factory = new OneContextFactory(runtime.ConnectionString, commands);
            var options = new ProjectionStatusOptions
            {
                ProvisioningMode = ProjectionStatusProvisioningMode.PreProvisioned
            };
            var store = new PostgresMultiProjectionStateStore(
                factory,
                new FixedServiceIdProvider("svc"),
                null,
                options);
            var first = Heartbeat("activation-a", 1) with
            {
                SwitchKind = "candidate",
                SwitchReason = "g63-proof",
                // PostgreSQL timestamp with time zone stores microseconds; this value is exactly representable.
                SwitchedAtUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1_234_560)
            };

            var created = await store.UpsertAsync(first, 0);
            var listed = await store.ListAsync();
            var updated = await store.UpsertAsync(first with { Sequence = 2, AppliedEventCount = 2 }, 1);
            var stale = await store.UpsertAsync(first with { Sequence = 3 }, 1);

            Assert.True(created.IsSuccess, created.IsSuccess ? string.Empty : created.GetException().ToString());
            Assert.True(created.GetValue().Committed);
            Assert.True(listed.IsSuccess, listed.IsSuccess ? string.Empty : listed.GetException().ToString());
            var listedHeartbeat = Assert.Single(listed.GetValue());
            Assert.Equal(first.SwitchKind, listedHeartbeat.SwitchKind);
            Assert.Equal(first.SwitchReason, listedHeartbeat.SwitchReason);
            Assert.Equal(first.SwitchedAtUtc, listedHeartbeat.SwitchedAtUtc);
            Assert.True(updated.IsSuccess);
            Assert.True(updated.GetValue().Committed);
            Assert.Equal(2, updated.GetValue().Current!.Sequence);
            Assert.True(stale.IsSuccess);
            Assert.Equal(ProjectionStatusConflictReason.SequenceMismatch, stale.GetValue().ConflictDetails!.Reason);

            var otherService = new PostgresMultiProjectionStateStore(
                factory,
                new FixedServiceIdProvider("other"),
                null,
                options);
            var isolated = await otherService.ListAsync();
            Assert.True(isolated.IsSuccess, isolated.IsSuccess ? string.Empty : isolated.GetException().ToString());
            Assert.Empty(isolated.GetValue());

            // The runtime role has DML only. Any attempted CREATE/ALTER path would fail with 42501 before this
            // already-successful DML-only sequence, and the wrapped ADO connection records the command attempts.
            Assert.NotEmpty(commands);
            Assert.DoesNotContain(commands, ContainsDdl);
        }
        finally
        {
            await DropRuntimePrincipalAsync(runtime.Role);
        }
    }

    [Fact]
    public async Task PreProvisionedOptions_ReachCreateForServiceAndDiStore_WithoutDdl()
    {
        await _fixture.DbContextFactory.ProvisionProjectionStatusSchemaAsync();
        var runtime = await CreateDmlOnlyRuntimePrincipalAsync(includeStatusTable: true);
        try
        {
            var commands = new List<string>();
            var factory = new OneContextFactory(runtime.ConnectionString, commands);
            var options = new ProjectionStatusOptions
            {
                ProvisioningMode = ProjectionStatusProvisioningMode.PreProvisioned
            };

            var factoryStore = new PostgresMultiProjectionStateStoreFactory(factory, null, options);
            var serviceStore = Assert.IsAssignableFrom<IProjectionStatusStore>(
                factoryStore.CreateForService("factory-service"));
            var factoryWrite = await serviceStore.UpsertAsync(
                Heartbeat("factory-activation", 1) with { ServiceId = "factory-service" },
                0);
            Assert.True(factoryWrite.IsSuccess, factoryWrite.IsSuccess ? string.Empty : factoryWrite.GetException().ToString());
            Assert.True(factoryWrite.GetValue().Committed);

            var services = new ServiceCollection();
            services.AddSingleton(options);
            services.AddSekibanDcbPostgres(runtime.ConnectionString);
            services.AddSingleton<IDbContextFactory<SekibanDcbDbContext>>(_ => factory);
            using var provider = services.BuildServiceProvider();
            var diStore = provider.GetRequiredService<IProjectionStatusStore>();
            var diWrite = await diStore.UpsertAsync(
                Heartbeat("di-activation", 1) with { ServiceId = DefaultServiceIdProvider.DefaultServiceId },
                0);
            Assert.True(diWrite.IsSuccess, diWrite.IsSuccess ? string.Empty : diWrite.GetException().ToString());
            Assert.True(diWrite.GetValue().Committed);

            // If either path drops PreProvisioned while forwarding options, legacy auto-provisioning attempts DDL
            // under this non-owner role and the write fails; the recording factory also proves no DDL was issued.
            Assert.NotEmpty(commands);
            Assert.DoesNotContain(commands, ContainsDdl);
        }
        finally
        {
            await DropRuntimePrincipalAsync(runtime.Role);
        }
    }

    [Fact]
    public async Task PreProvisionedRuntime_MissingTable_Returns42P01WithoutDdlOrFalseSuccess()
    {
        var runtime = await CreateDmlOnlyRuntimePrincipalAsync(includeStatusTable: false);
        try
        {
            var commands = new List<string>();
            var store = new PostgresMultiProjectionStateStore(
                new OneContextFactory(runtime.ConnectionString, commands),
                new FixedServiceIdProvider("svc"),
                null,
                new ProjectionStatusOptions { ProvisioningMode = ProjectionStatusProvisioningMode.PreProvisioned });

            var result = await store.UpsertAsync(Heartbeat("activation-missing", 1), 0);

            Assert.False(result.IsSuccess);
            var failure = Assert.IsType<PostgresException>(result.GetException());
            Assert.Equal(PostgresErrorCodes.UndefinedTable, failure.SqlState); // SQLSTATE 42P01
            // A DDL attempt by this role would be 42501; 42P01 is the direct DML failure with no auto-provisioning.
            Assert.NotEmpty(commands);
            Assert.DoesNotContain(commands, ContainsDdl);
            var list = await store.ListAsync();
            Assert.False(list.IsSuccess);
            Assert.Equal(PostgresErrorCodes.UndefinedTable, Assert.IsType<PostgresException>(list.GetException()).SqlState);
        }
        finally
        {
            await DropRuntimePrincipalAsync(runtime.Role);
        }
    }

    [Fact]
    public async Task PreProvisionedRuntime_MissingColumn_Returns42703WithoutDdlOrFalseSuccess()
    {
        await _fixture.DbContextFactory.ProvisionProjectionStatusSchemaAsync();
        await using (var admin = new NpgsqlConnection(_fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await ExecuteAsync(admin, "ALTER TABLE dcb_projection_statuses DROP COLUMN phase");
        }

        var runtime = await CreateDmlOnlyRuntimePrincipalAsync(includeStatusTable: true);
        try
        {
            var commands = new List<string>();
            var store = new PostgresMultiProjectionStateStore(
                new OneContextFactory(runtime.ConnectionString, commands),
                new FixedServiceIdProvider("svc"),
                null,
                new ProjectionStatusOptions { ProvisioningMode = ProjectionStatusProvisioningMode.PreProvisioned });

            var result = await store.UpsertAsync(Heartbeat("activation-missing-column", 1), 0);

            Assert.False(result.IsSuccess);
            var failure = Assert.IsType<PostgresException>(result.GetException());
            Assert.Equal(PostgresErrorCodes.UndefinedColumn, failure.SqlState); // SQLSTATE 42703
            // A DDL attempt by this role would be 42501; 42703 is the direct DML failure with no auto-repair.
            Assert.NotEmpty(commands);
            Assert.DoesNotContain(commands, ContainsDdl);
            var list = await store.ListAsync();
            Assert.False(list.IsSuccess);
            Assert.Equal(PostgresErrorCodes.UndefinedColumn, Assert.IsType<PostgresException>(list.GetException()).SqlState);
        }
        finally
        {
            await DropRuntimePrincipalAsync(runtime.Role);
        }
    }

    [Fact]
    public void LegacyConstructorsAndDiRegistrationRemainAdditive()
    {
        var legacyStoreConstructor = typeof(PostgresMultiProjectionStateStore).GetConstructor(
            [
                typeof(IDbContextFactory<SekibanDcbDbContext>),
                typeof(IServiceIdProvider),
                typeof(Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor)
            ]);
        var legacyFactoryConstructor = typeof(PostgresMultiProjectionStateStoreFactory).GetConstructor(
            [
                typeof(IDbContextFactory<SekibanDcbDbContext>),
                typeof(Sekiban.Dcb.Snapshots.IBlobStorageSnapshotAccessor)
            ]);

        Assert.NotNull(legacyStoreConstructor);
        Assert.NotNull(legacyFactoryConstructor);

        var services = new ServiceCollection();
        services.AddSekibanDcbPostgres(_fixture.ConnectionString);
        services.AddSingleton(new ProjectionStatusOptions
        {
            ProvisioningMode = ProjectionStatusProvisioningMode.PreProvisioned
        });
        using var provider = services.BuildServiceProvider();
        Assert.IsType<PostgresMultiProjectionStateStore>(provider.GetRequiredService<IMultiProjectionStateStore>());
        Assert.IsType<PostgresMultiProjectionStateStoreFactory>(provider.GetRequiredService<IMultiProjectionStateStoreFactory>());
    }

    [Fact]
    public void InvalidProvisioningModeIsRejectedBeforeStatusWork()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PostgresMultiProjectionStateStore(
            _fixture.DbContextFactory,
            new FixedServiceIdProvider("svc"),
            null,
            new ProjectionStatusOptions { ProvisioningMode = (ProjectionStatusProvisioningMode)99 }));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static bool ContainsDdl(string sql) =>
        sql.Contains("CREATE", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("ALTER", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("DROP", StringComparison.OrdinalIgnoreCase) ||
        sql.Contains("MIGRAT", StringComparison.OrdinalIgnoreCase);

    private async Task<RuntimePrincipal> CreateDmlOnlyRuntimePrincipalAsync(bool includeStatusTable)
    {
        var role = $"g63_runtime_dml_{Guid.NewGuid():N}";
        var password = $"g63_runtime_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(_fixture.ConnectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"CREATE ROLE {role} LOGIN PASSWORD '{password}'");
        await ExecuteAsync(admin, $"GRANT USAGE ON SCHEMA public TO {role}");
        if (includeStatusTable)
        {
            await ExecuteAsync(admin, $"GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE dcb_projection_statuses TO {role}");
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            Username = role,
            Password = password
        }.ConnectionString;
        return new RuntimePrincipal(role, connectionString);
    }

    private async Task DropRuntimePrincipalAsync(string role)
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_fixture.ConnectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"DROP OWNED BY {role}");
        await ExecuteAsync(admin, $"DROP ROLE IF EXISTS {role}");
    }

    private sealed record RuntimePrincipal(string Role, string ConnectionString);

    private sealed class OneContextFactory : IDbContextFactory<SekibanDcbDbContext>
    {
        private readonly string _connection;
        private readonly List<string>? _commands;

        public OneContextFactory(string connection, List<string>? commands = null)
        {
            _connection = connection;
            _commands = commands;
        }

        public SekibanDcbDbContext CreateDbContext()
        {
            var builder = new DbContextOptionsBuilder<SekibanDcbDbContext>();
            if (_commands is null)
            {
                builder.UseNpgsql(_connection);
            }
            else
            {
                builder.UseNpgsql(new RecordingDbConnection(new NpgsqlConnection(_connection), _commands), true);
            }

            return new SekibanDcbDbContext(builder.Options);
        }
    }

    // The framework's legacy DbConnection/DbCommand setters carry nullable metadata that differs by target TFM;
    // this test-only delegating observer preserves the provider's concrete command while recording every ADO call.
#pragma warning disable CS8765
    private sealed class RecordingDbConnection : DbConnection
    {
        private readonly DbConnection _inner;
        private readonly List<string> _commands;

        public RecordingDbConnection(DbConnection inner, List<string> commands)
        {
            _inner = inner;
            _commands = commands;
        }

        public override string ConnectionString
        {
            get => _inner.ConnectionString;
            set => _inner.ConnectionString = value;
        }

        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
        public override void Close() => _inner.Close();
        public override void Open() => _inner.Open();
        public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            _inner.BeginTransaction(isolationLevel);

        protected override DbCommand CreateDbCommand() =>
            new RecordingDbCommand(_inner.CreateCommand(), this, _commands);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class RecordingDbCommand : DbCommand
    {
        private readonly DbCommand _inner;
        private readonly DbConnection _connection;
        private readonly List<string> _commands;

        public RecordingDbCommand(DbCommand inner, DbConnection connection, List<string> commands)
        {
            _inner = inner;
            _connection = connection;
            _commands = commands;
        }

        public override string CommandText
        {
            get => _inner.CommandText;
            set => _inner.CommandText = value;
        }

        public override int CommandTimeout
        {
            get => _inner.CommandTimeout;
            set => _inner.CommandTimeout = value;
        }

        public override CommandType CommandType
        {
            get => _inner.CommandType;
            set => _inner.CommandType = value;
        }

        public override bool DesignTimeVisible
        {
            get => _inner.DesignTimeVisible;
            set => _inner.DesignTimeVisible = value;
        }

        public override UpdateRowSource UpdatedRowSource
        {
            get => _inner.UpdatedRowSource;
            set => _inner.UpdatedRowSource = value;
        }

        protected override DbConnection DbConnection
        {
            get => _connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

        protected override DbTransaction? DbTransaction
        {
            get => _inner.Transaction;
            set => _inner.Transaction = value;
        }

        public override void Cancel() => _inner.Cancel();
        public override int ExecuteNonQuery()
        {
            Record();
            return _inner.ExecuteNonQuery();
        }

        public override object? ExecuteScalar()
        {
            Record();
            return _inner.ExecuteScalar();
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            Record();
            return _inner.ExecuteNonQueryAsync(cancellationToken);
        }

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            Record();
            return _inner.ExecuteScalarAsync(cancellationToken);
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Record();
            return _inner.ExecuteReader(behavior);
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken)
        {
            Record();
            return _inner.ExecuteReaderAsync(behavior, cancellationToken);
        }

        public override void Prepare() => _inner.Prepare();

        protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Record() => _commands.Add(CommandText);
    }
#pragma warning restore CS8765

    private static ProjectionStatusHeartbeat Heartbeat(string activationId, long sequence) =>
        new(
            "svc",
            "status-projector",
            "v1",
            "cluster-a",
            activationId,
            sequence,
            sequence,
            null,
            null,
            DateTimeOffset.UtcNow)
        {
            Phase = ProjectionStatusPhases.Active
        };
}
