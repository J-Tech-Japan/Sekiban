using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     SEK-G40 SQL-policy proofs are deliberately split: a DDL-denied runtime principal, that principal's allowed DML on
///     a provisioned schema, and an unprovisioned-schema fail-closed call whose captured runtime SQL contains no DDL.
/// </summary>
public sealed class PostgresTagHeadSqlPolicyTests : PostgresTestBase
{
    public PostgresTagHeadSqlPolicyTests(PostgresTestFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ProvisioningMigrationAndModel_PinServiceScopedHeadEpochAndViolationTables()
    {
        await using var context = await Fixture.GetDbContextAsync();
        var model = context.GetService<IDesignTimeModel>().Model;
        var heads = model.FindEntityType(typeof(DbTagHead))
            ?? throw new InvalidOperationException("Tag-head model is missing.");
        var violations = model.FindEntityType(typeof(DbTagHeadViolation))
            ?? throw new InvalidOperationException("Tag-head-violation model is missing.");
        var epochs = model.FindEntityType(typeof(DbTagHeadEnablementEpoch))
            ?? throw new InvalidOperationException("Tag-head epoch model is missing.");

        Assert.Equal("dcb_tag_heads", heads.GetTableName());
        Assert.Equal(new[] { "ServiceId", "Tag" }, heads.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());
        Assert.Contains(heads.GetCheckConstraints(), c => c.Name == "CK_TagHeads_Position_NotEmpty");
        Assert.Equal("dcb_tag_head_violations", violations.GetTableName());
        Assert.Contains(violations.GetIndexes(), index =>
            index.IsUnique && index.GetDatabaseName() == "UX_TagHeadViolations_IdempotentRepair");
        Assert.Equal("dcb_tag_head_enablement_epochs", epochs.GetTableName());
        Assert.Equal(new[] { "ServiceId" }, epochs.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());

        var migrationNames = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Contains(migrationNames, name => name.EndsWith("AddPostgresTagHeadExpectedPositionCas", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DmlOnlyRuntimePrincipal_IsDeniedRuntimeDdlWithSqlState42501()
    {
        var runtime = await CreateDmlOnlyRuntimePrincipalAsync();
        try
        {
            await using var connection = new NpgsqlConnection(runtime.ConnectionString);
            await connection.OpenAsync();
            var ddl = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteAsync(connection, "CREATE TABLE g40_runtime_must_not_create (id integer)"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, ddl.SqlState); // SQLSTATE 42501
        }
        finally
        {
            await DropRuntimePrincipalAsync(runtime.Role);
        }
    }

    [Fact]
    public async Task DmlOnlyRuntimePrincipal_CanRunProvisionedHeadDml()
    {
        var runtime = await CreateDmlOnlyRuntimePrincipalAsync();
        try
        {
            var factory = new OneContextFactory(runtime.ConnectionString);
            var store = new PostgresEventStore(
                factory,
                Fixture.DomainTypes.EventTypes,
                new FixedServiceIdProvider(DefaultServiceIdProvider.DefaultServiceId));
            var write = await store.WriteSerializableEventsWithExpectedTagPositionsAsync(
                [new SerializableEvent(
                    "{}"u8.ToArray(), "7000", Guid.CreateVersion7(), new EventMetadata("c", "r", "u"),
                    ["Order:dml-only"], "G40Marker")],
                new ExpectedTagPositionSpecification(
                    [new TagHeadExpectationEntry(DefaultServiceIdProvider.DefaultServiceId, "Order:dml-only", TagHeadExpectation.NoEnforcement())]));
            Assert.True(write.IsSuccess, write.IsSuccess ? "" : write.GetException().ToString());
        }
        finally
        {
            await DropRuntimePrincipalAsync(runtime.Role);
        }
    }

    [Fact]
    public async Task UnprovisionedSchema_FailsClosedWith42P01_AndRuntimeEmitsNoDdl()
    {
        const string schema = "g40_unprovisioned";
        await using var admin = new NpgsqlConnection(Fixture.ConnectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"DROP SCHEMA IF EXISTS {schema} CASCADE");
        await ExecuteAsync(admin, $"CREATE SCHEMA {schema}");
        try
        {
            var recording = new RecordingCommandInterceptor();
            var missingConnection = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
            {
                SearchPath = schema
            }.ConnectionString;
            var store = new PostgresEventStore(
                new OneContextFactory(missingConnection, recording),
                Fixture.DomainTypes.EventTypes,
                new FixedServiceIdProvider(DefaultServiceIdProvider.DefaultServiceId));

            var result = await store.EnsureExpectedTagPositionEnforcementEnabledAsync();

            Assert.False(result.IsSuccess);
            var failure = Assert.IsType<PostgresException>(result.GetException());
            Assert.Equal(PostgresErrorCodes.UndefinedTable, failure.SqlState); // SQLSTATE 42P01
            Assert.NotEmpty(recording.Commands);
            Assert.DoesNotContain(recording.Commands, sql =>
                sql.Contains("CREATE", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("ALTER", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("MIGRAT", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            await ExecuteAsync(admin, $"DROP SCHEMA IF EXISTS {schema} CASCADE");
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<RuntimePrincipal> CreateDmlOnlyRuntimePrincipalAsync()
    {
        var role = $"g40_runtime_dml_{Guid.NewGuid():N}";
        const string password = "g40_runtime_dml_password";
        await using var admin = new NpgsqlConnection(Fixture.ConnectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"CREATE ROLE {role} LOGIN PASSWORD '{password}'");
        await ExecuteAsync(admin, $"GRANT USAGE ON SCHEMA public TO {role}");
        await ExecuteAsync(admin,
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE dcb_events, dcb_tags, dcb_tag_heads, dcb_tag_head_violations, dcb_tag_head_enablement_epochs TO {role}");
        await ExecuteAsync(admin, $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {role}");
        var connectionString = new NpgsqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Username = role,
            Password = password
        }.ConnectionString;
        return new RuntimePrincipal(role, connectionString);
    }

    private async Task DropRuntimePrincipalAsync(string role)
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Fixture.ConnectionString);
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"DROP OWNED BY {role}");
        await ExecuteAsync(admin, $"DROP ROLE IF EXISTS {role}");
    }

    private sealed record RuntimePrincipal(string Role, string ConnectionString);

    private sealed class OneContextFactory : IDbContextFactory<SekibanDcbDbContext>
    {
        private readonly string _connection;
        private readonly IInterceptor[] _interceptors;

        public OneContextFactory(string connection, params IInterceptor[] interceptors)
        {
            _connection = connection;
            _interceptors = interceptors;
        }

        public SekibanDcbDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<SekibanDcbDbContext>()
                .UseNpgsql(_connection)
                .AddInterceptors(_interceptors)
                .Options);
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
