using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvVerifyOnlyAndPolicyTests(SqliteMvFixture fixture)
{
    [SkippableFact]
    public async Task VerifyOnly_UsesPreProvisionedSchema_OnReadOnlyConnection_WithoutDdl()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);

        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        await fixture.Executor.InitializeAsync(CreateHost(projector)).ConfigureAwait(false);
        var schemaVersionBefore = await ReadSchemaVersionAsync().ConfigureAwait(false);
        var readOnlyConnectionString = new SqliteConnectionStringBuilder(fixture.ConnectionStringForTests)
        {
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();
        var policy = new RecordingPolicy(_ => MvSqlPolicyDecision.Allow());
        var verifyExecutor = CreateExecutor(
            readOnlyConnectionString,
            MvInitializationMode.VerifyOnly,
            policy,
            rejectEnsureInfrastructure: true);

        var verifyHost = new CountingApplyHost(CreateHost(projector), throwOnInitialize: true);
        await verifyExecutor.InitializeAsync(verifyHost).ConfigureAwait(false);

        var schemaVersionAfter = await ReadSchemaVersionAsync().ConfigureAwait(false);
        Assert.Equal(schemaVersionBefore, schemaVersionAfter);
        Assert.Equal(0, verifyHost.InitializeCalls);
        Assert.Empty(policy.Contexts);
    }

    [SkippableFact]
    public async Task VerifyOnly_MissingTargetFailsBeforeRegistryMutation()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var registryStore = fixture.Services.GetRequiredService<IMvRegistryStore>();
        await registryStore.EnsureInfrastructureAsync().ConfigureAwait(false);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var verifyExecutor = CreateExecutor(fixture.ConnectionStringForTests, MvInitializationMode.VerifyOnly, new RecordingPolicy(_ => MvSqlPolicyDecision.Allow()));

        var exception = await Assert.ThrowsAsync<MvInitializationException>(
            () => verifyExecutor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.MissingSchemaObject, exception.Failure.Reason);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sekiban_mv_registry;"));
    }

    [SkippableFact]
    public async Task VerifyOnly_MissingFrameworkRegistryFailsClosedBeforeAnyMutation()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var verifyExecutor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.VerifyOnly,
            new RecordingPolicy(_ => MvSqlPolicyDecision.Allow()));

        var exception = await Assert.ThrowsAsync<MvInitializationException>(
            () => verifyExecutor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.MissingSchemaObject, exception.Failure.Reason);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'sekiban_mv_%';"));
    }

    [SkippableFact]
    public async Task VerifyOnly_IncompatibleColumnFailsClosedBeforeRegistryMutation()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var registryStore = fixture.Services.GetRequiredService<IMvRegistryStore>();
        await registryStore.EnsureInfrastructureAsync().ConfigureAwait(false);
        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync(
                """
                CREATE TABLE sekiban_mv_weatherforecastportable_v1_forecasts (
                    forecast_id TEXT NOT NULL PRIMARY KEY,
                    location TEXT NOT NULL,
                    forecast_date TEXT NOT NULL,
                    temperature_c TEXT NOT NULL,
                    summary TEXT NULL,
                    is_deleted INTEGER NOT NULL DEFAULT 0,
                    _last_sortable_unique_id TEXT NOT NULL,
                    _last_applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                """).ConfigureAwait(false);
        }

        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var verifyExecutor = CreateExecutor(fixture.ConnectionStringForTests, MvInitializationMode.VerifyOnly, new RecordingPolicy(_ => MvSqlPolicyDecision.Allow()));
        var exception = await Assert.ThrowsAsync<MvInitializationException>(
            () => verifyExecutor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.IncompatibleSchema, exception.Failure.Reason);
        await using var checkConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await checkConnection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sekiban_mv_registry;"));
    }

    [SkippableFact]
    public async Task VerifyOnly_MissingRegistryBindingFailsClosedWithoutRegistryFallback()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var registryStore = fixture.Services.GetRequiredService<IMvRegistryStore>();
        await registryStore.EnsureInfrastructureAsync().ConfigureAwait(false);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        await fixture.Executor.InitializeAsync(CreateHost(projector)).ConfigureAwait(false);

        await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await connection.ExecuteAsync("DELETE FROM sekiban_mv_registry;").ConfigureAwait(false);
        }

        var verifyExecutor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.VerifyOnly,
            new RecordingPolicy(_ => MvSqlPolicyDecision.Allow()),
            rejectEnsureInfrastructure: true);

        var exception = await Assert.ThrowsAsync<MvInitializationException>(
            () => verifyExecutor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.MissingSchemaContract, exception.Failure.Reason);
        await using var checkConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await checkConnection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sekiban_mv_registry;"));
    }

    [SkippableFact]
    public async Task VerifyOnly_WithoutReadOnlyInspectorFailsBeforeAnyRegistryOrHostIo()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var registry = new NoInspectorRegistryStore(fixture.Services.GetRequiredService<IMvRegistryStore>());
        var executor = new SqliteMvExecutor(
            fixture.EventStoreFactory,
            registry,
            Options.Create(new MvOptions
            {
                ServiceId = MultiProviderFixtureBase.ServiceId,
                InitializationMode = MvInitializationMode.VerifyOnly,
                SafeWindowMs = 0,
                BatchSize = 100
            }),
            NullLogger<SqliteMvExecutor>.Instance,
            fixture.ConnectionStringForTests);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var host = new CountingApplyHost(CreateHost(projector), throwOnInitialize: true);

        var exception = await Assert.ThrowsAsync<MvInitializationException>(
            () => executor.InitializeAsync(host)).ConfigureAwait(false);

        Assert.Equal(MvInitializationFailureReason.UnsupportedProviderCapability, exception.Failure.Reason);
        Assert.Equal(0, host.InitializeCalls);
        Assert.Equal(0, registry.EnsureCalls);
        Assert.Equal(0, registry.RegisterCalls);
        Assert.Equal(0, registry.GetEntriesCalls);
    }

    [SkippableFact]
    public async Task VerifyOnly_ImplicitEmptyRegistryPathsUseTheSameFailClosedGate()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        var pathNames = new[] { "target-capture", "catch-up", "apply" };
        foreach (var pathName in pathNames)
        {
            await fixture.ResetAsync().ConfigureAwait(false);
            var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
            await fixture.Executor.InitializeAsync(CreateHost(projector)).ConfigureAwait(false);
            await using (var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
            {
                await connection.ExecuteAsync("DELETE FROM sekiban_mv_registry;").ConfigureAwait(false);
                Assert.Equal(0, await connection.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM sekiban_mv_registry;").ConfigureAwait(false));
            }

            var verifyExecutor = CreateExecutor(
                fixture.ConnectionStringForTests,
                MvInitializationMode.VerifyOnly,
                new RecordingPolicy(_ => MvSqlPolicyDecision.Allow()),
                rejectEnsureInfrastructure: true);
            var host = new CountingApplyHost(CreateHost(projector), throwOnInitialize: true);

            var exception = pathName switch
            {
                "target-capture" => await Assert.ThrowsAsync<MvInitializationException>(
                    () => verifyExecutor.CaptureTargetCheckpointAsync(host)).ConfigureAwait(false),
                "catch-up" => await Assert.ThrowsAsync<MvInitializationException>(
                    async () => await verifyExecutor.CatchUpOnceAsync(host).ConfigureAwait(false)).ConfigureAwait(false),
                _ => await Assert.ThrowsAsync<MvInitializationException>(
                    () => verifyExecutor.ApplySerializableEventsAsync(host, [])).ConfigureAwait(false)
            };

            Assert.Equal(MvInitializationFailureReason.MissingSchemaContract, exception.Failure.Reason);
            Assert.Equal(0, host.InitializeCalls);
            await using var checkConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
            Assert.Equal(0, await checkConnection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sekiban_mv_registry;"));
        }
    }

    [SkippableFact]
    public async Task VerifyOnly_ImplicitPathsReportMissingFrameworkSchemaBeforeRegistryRead()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        var pathNames = new[] { "target-capture", "catch-up", "apply" };
        foreach (var pathName in pathNames)
        {
            await fixture.ResetAsync().ConfigureAwait(false);
            var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
            var verifyExecutor = CreateExecutor(
                fixture.ConnectionStringForTests,
                MvInitializationMode.VerifyOnly,
                new RecordingPolicy(_ => MvSqlPolicyDecision.Allow()),
                rejectEnsureInfrastructure: true);
            var host = new CountingApplyHost(CreateHost(projector), throwOnInitialize: true);

            var exception = pathName switch
            {
                "target-capture" => await Assert.ThrowsAsync<MvInitializationException>(
                    () => verifyExecutor.CaptureTargetCheckpointAsync(host)).ConfigureAwait(false),
                "catch-up" => await Assert.ThrowsAsync<MvInitializationException>(
                    async () => await verifyExecutor.CatchUpOnceAsync(host).ConfigureAwait(false)).ConfigureAwait(false),
                _ => await Assert.ThrowsAsync<MvInitializationException>(
                    () => verifyExecutor.ApplySerializableEventsAsync(host, [])).ConfigureAwait(false)
            };

            Assert.Equal(MvInitializationFailureReason.MissingSchemaObject, exception.Failure.Reason);
            Assert.Equal(0, host.InitializeCalls);
            await using var checkConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
            Assert.Equal(
                0,
                await checkConnection.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'sekiban_mv_%';"));
        }
    }

    [SkippableFact]
    public async Task InitializationPolicyRejectsBeforeProviderCommands()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var policy = new RecordingPolicy(_ => MvSqlPolicyDecision.Reject("DDL is not approved for this host."));
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy,
            policyMode: MvSqlStatementPolicyMode.Enforced);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();

        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvSqlStatementPhase.Initialization, exception.Failure.Phase);
        Assert.Single(policy.Contexts);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'sekiban_mv_%';"));
    }

    [SkippableFact]
    public async Task EnforcedPolicyWithoutRegistrationFailsClosedBeforeProviderCommands()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy: null,
            policyMode: MvSqlStatementPolicyMode.Enforced);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();

        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.InitializeAsync(CreateHost(projector))).ConfigureAwait(false);

        Assert.Equal(MvSqlPolicyFailureReason.PolicyUnavailable, exception.Failure.FailureReason);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'sekiban_mv_%';"));
    }

    [SkippableFact]
    public async Task EnforcedPolicyInvalidDecisionFailsClosedBeforeProviderCommands()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await ResetPolicySurfaceAsync().ConfigureAwait(false);
        var projector = new PolicySurfaceProjector(PolicySurfaceKind.Rows, "SELECT id FROM records");
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);
        var policy = new RecordingPolicy(context =>
            context.Phase == MvSqlStatementPhase.Initialization
                ? MvSqlPolicyDecision.Allow()
                : new MvSqlPolicyDecision(false, null));
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy,
            policyMode: MvSqlStatementPolicyMode.Enforced);

        await executor.InitializeAsync(host).ConfigureAwait(false);
        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.ApplySerializableEventsAsync(host, [CreatePolicyEvent()])).ConfigureAwait(false);

        Assert.Equal(MvSqlPolicyFailureReason.InvalidDecision, exception.Failure.FailureReason);
        await AssertPolicySurfaceHasNoAppliedEventAsync(projector).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task ApplyPolicyRejectsBeforeProviderMutation()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        await fixture.Executor.InitializeAsync(CreateHost(projector)).ConfigureAwait(false);
        await using (var guardConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false))
        {
            await guardConnection.ExecuteAsync(
                $"""
                CREATE TRIGGER mv_policy_execution_guard
                BEFORE INSERT ON {MultiProviderFixtureBase.ForecastTable}
                BEGIN
                    SELECT RAISE(ABORT, 'policy test reached provider execution');
                END;
                """).ConfigureAwait(false);
        }
        var policy = new RecordingPolicy(context =>
            context.Phase == MvSqlStatementPhase.Apply
                ? MvSqlPolicyDecision.Reject("Apply is not approved for this host.")
                : MvSqlPolicyDecision.Allow());
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy,
            policyMode: MvSqlStatementPolicyMode.Enforced);
        var forecastId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var eventValue = new Event(
            new WeatherForecastCreated(
                forecastId,
                "Tokyo",
                DateOnly.FromDateTime(DateTime.UtcNow),
                20,
                "Sunny"),
            SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-1), eventId),
            nameof(WeatherForecastCreated),
            eventId,
            new EventMetadata("policy-test", "policy-test", "test"),
            []);
        var serializableEvent = eventValue.ToSerializableEvent(fixture.DomainTypes.EventTypes);

        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.ApplySerializableEventsAsync(CreateHost(projector), [serializableEvent])).ConfigureAwait(false);

        Assert.Equal(MvSqlStatementPhase.Apply, exception.Failure.Phase);
        Assert.Equal(MvSqlPolicyFailureReason.Denied, exception.Failure.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(exception.Failure.SqlSha256));
        var applyContext = Assert.Single(policy.Contexts);
        Assert.Equal(MvSqlStatementPhase.Apply, applyContext.Phase);
        Assert.Equal(MultiProviderFixtureBase.ServiceId, applyContext.ServiceId);
        Assert.Equal(projector.ViewName, applyContext.ViewName);
        Assert.Equal(projector.ViewVersion, applyContext.ViewVersion);
        var applyTable = Assert.Single(applyContext.Tables);
        Assert.Equal("forecasts", applyTable.LogicalName);
        Assert.Equal(projector.Forecasts.PhysicalName, applyTable.PhysicalName);
        Assert.Contains(
            applyContext.Parameters,
            parameter => parameter.Name == "ForecastId" && parameter.Kind == MvParamKind.String);
        Assert.All(applyContext.Parameters, parameter => Assert.Null(parameter.ValueJson));
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sekiban_mv_weatherforecastportable_v1_forecasts;"));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                """
                SELECT applied_event_version
                FROM sekiban_mv_registry
                WHERE service_id = @ServiceId
                  AND view_name = 'WeatherForecastPortable'
                  AND logical_table = 'forecasts';
                """,
                new { ServiceId = MultiProviderFixtureBase.ServiceId }));
    }

    [SkippableTheory]
    [InlineData("WITH changed AS (UPDATE records SET value = 'x' RETURNING id) SELECT id FROM changed")]
    [InlineData("/* policy probe */ SELECT id FROM records")]
    [InlineData("SELECT id FROM records; UPDATE records SET value = 'x'")]
    public async Task EnforcedPolicyRejectsMutatingCommentedAndMultiStatementQueriesBeforeProvider(
        string sql)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await ResetPolicySurfaceAsync().ConfigureAwait(false);
        var projector = new PolicySurfaceProjector(PolicySurfaceKind.Rows, sql);
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);
        var policy = new RecordingPolicy(context =>
            context.Phase == MvSqlStatementPhase.Initialization
                ? MvSqlPolicyDecision.Allow()
                : MvSqlPolicyDecision.Reject("Query shape is not allowlisted."));
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy,
            policyMode: MvSqlStatementPolicyMode.Enforced);

        await executor.InitializeAsync(host).ConfigureAwait(false);
        var serializableEvent = CreatePolicyEvent();
        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.ApplySerializableEventsAsync(host, [serializableEvent])).ConfigureAwait(false);

        Assert.Equal(MvSqlStatementPhase.Apply, exception.Failure.Phase);
        var queryContext = Assert.Single(policy.Contexts, context => context.Phase == MvSqlStatementPhase.Apply);
        Assert.Equal(sql, queryContext.Sql);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {projector.Records.PhysicalName};"));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT applied_event_version FROM sekiban_mv_registry WHERE view_name = 'PolicySurface';"));
    }

    [SkippableTheory]
    [InlineData(PolicySurfaceKind.Rows)]
    [InlineData(PolicySurfaceKind.Single)]
    [InlineData(PolicySurfaceKind.Scalar)]
    public async Task EnforcedPolicyGatesEveryQueryPortSurfaceBeforeProviderExecution(PolicySurfaceKind surface)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await ResetPolicySurfaceAsync().ConfigureAwait(false);
        var projector = new PolicySurfaceProjector(surface, "SELECT id FROM records");
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);
        var policy = new RecordingPolicy(context =>
            context.Phase == MvSqlStatementPhase.Initialization
                ? MvSqlPolicyDecision.Allow()
                : MvSqlPolicyDecision.Reject("Apply query is not approved."));
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy,
            policyMode: MvSqlStatementPolicyMode.Enforced);

        await executor.InitializeAsync(host).ConfigureAwait(false);
        await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.ApplySerializableEventsAsync(host, [CreatePolicyEvent()])).ConfigureAwait(false);

        Assert.Single(policy.Contexts, context => context.Phase == MvSqlStatementPhase.Apply);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {projector.Records.PhysicalName};"));
    }

    [SkippableFact]
    public async Task EnforcedPolicyRejectsRawConnectionBypassWithoutProviderExecution()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await ResetPolicySurfaceAsync().ConfigureAwait(false);
        var projector = new PolicySurfaceProjector(PolicySurfaceKind.Raw, "");
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);
        var policy = new RecordingPolicy(context =>
            context.Phase == MvSqlStatementPhase.Initialization
                ? MvSqlPolicyDecision.Allow()
                : MvSqlPolicyDecision.Allow());
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy,
            policyMode: MvSqlStatementPolicyMode.Enforced);

        await executor.InitializeAsync(host).ConfigureAwait(false);
        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.ApplySerializableEventsAsync(host, [CreatePolicyEvent()])).ConfigureAwait(false);

        Assert.Equal(MvSqlStatementPhase.Apply, exception.Failure.Phase);
        Assert.Contains("raw connection", exception.Failure.Reason, StringComparison.OrdinalIgnoreCase);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {projector.Records.PhysicalName};"));
    }

    [SkippableFact]
    public async Task EnforcedPolicyPreflightsWholeApplyBatchBeforeAnyProviderCommand()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await ResetPolicySurfaceAsync().ConfigureAwait(false);
        var projector = new PolicySurfaceProjector(PolicySurfaceKind.Batch, "");
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);
        var policy = new RecordingPolicy(context =>
        {
            if (context.Phase == MvSqlStatementPhase.Initialization)
            {
                return MvSqlPolicyDecision.Allow();
            }

            return context.Sql.Contains("second", StringComparison.OrdinalIgnoreCase)
                ? MvSqlPolicyDecision.Reject("The second apply statement is not approved.")
                : MvSqlPolicyDecision.Allow();
        });
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy,
            policyMode: MvSqlStatementPolicyMode.Enforced);

        await executor.InitializeAsync(host).ConfigureAwait(false);
        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.ApplySerializableEventsAsync(host, [CreatePolicyEvent()])).ConfigureAwait(false);

        Assert.Equal(MvSqlStatementPhase.Apply, exception.Failure.Phase);
        Assert.Equal(2, policy.Contexts.Count(context => context.Phase == MvSqlStatementPhase.Apply));
        Assert.Equal([0, 1], policy.Contexts
            .Where(context => context.Phase == MvSqlStatementPhase.Apply)
            .Select(context => context.StatementIndex));
        Assert.All(
            policy.Contexts.Where(context => context.Phase == MvSqlStatementPhase.Apply),
            context => Assert.Equal(2, context.BatchSize));
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {projector.Records.PhysicalName};"));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT applied_event_version FROM sekiban_mv_registry WHERE view_name = 'PolicySurface';"));
    }

    [SkippableFact]
    public async Task EnforcedPolicyFaultFailsClosedWithDistinctTypedReason()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await ResetPolicySurfaceAsync().ConfigureAwait(false);
        var projector = new PolicySurfaceProjector(PolicySurfaceKind.Rows, "SELECT id FROM records");
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);
        var policy = new FaultingPolicy();
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy,
            policyMode: MvSqlStatementPolicyMode.Enforced);

        await executor.InitializeAsync(host).ConfigureAwait(false);
        var exception = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
            () => executor.ApplySerializableEventsAsync(host, [CreatePolicyEvent()])).ConfigureAwait(false);

        Assert.Equal(MvSqlPolicyFailureReason.PolicyEvaluationFailed, exception.Failure.FailureReason);
        await AssertPolicySurfaceHasNoAppliedEventAsync(projector).ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task EnforcedPolicyCancellationRemainsOperationCanceledAndDoesNotExecute()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await ResetPolicySurfaceAsync().ConfigureAwait(false);
        var projector = new PolicySurfaceProjector(PolicySurfaceKind.Rows, "SELECT id FROM records");
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);
        using var cancellation = new CancellationTokenSource();
        var policy = new CancellingPolicy(cancellation);
        var executor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.CreateOrEnsure,
            policy,
            policyMode: MvSqlStatementPolicyMode.Enforced);

        await executor.InitializeAsync(host).ConfigureAwait(false);
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ApplySerializableEventsAsync(host, [CreatePolicyEvent()], cancellationToken: cancellation.Token))
            .ConfigureAwait(false);

        await AssertPolicySurfaceHasNoAppliedEventAsync(projector).ConfigureAwait(false);
    }

    private async Task AssertPolicySurfaceHasNoAppliedEventAsync(PolicySurfaceProjector projector)
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {projector.Records.PhysicalName};"));
        Assert.Equal(
            0,
            await connection.ExecuteScalarAsync<long>(
                "SELECT applied_event_version FROM sekiban_mv_registry WHERE view_name = 'PolicySurface';"));
    }

    private async Task ResetPolicySurfaceAsync()
    {
        await fixture.ResetAsync().ConfigureAwait(false);
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        await connection.ExecuteAsync("DROP TABLE IF EXISTS sekiban_mv_policysurface_v1_records;").ConfigureAwait(false);
    }

    private SerializableEvent CreatePolicyEvent()
    {
        var eventId = Guid.CreateVersion7();
        var eventValue = new Event(
            new WeatherForecastCreated(
                Guid.CreateVersion7(),
                "Tokyo",
                DateOnly.FromDateTime(DateTime.UtcNow),
                20,
                "Sunny"),
            SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-1), eventId),
            nameof(WeatherForecastCreated),
            eventId,
            new EventMetadata("policy-test", "policy-test", "test"),
            []);
        return eventValue.ToSerializableEvent(fixture.DomainTypes.EventTypes);
    }

    private NativeMvApplyHost CreateHost(CrossProviderWeatherForecastMvV1 projector) =>
        new(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);

    private SqliteMvExecutor CreateExecutor(
        string connectionString,
        MvInitializationMode initializationMode,
        IMvSqlStatementPolicy? policy,
        bool rejectEnsureInfrastructure = false,
        MvSqlStatementPolicyMode policyMode = MvSqlStatementPolicyMode.Legacy)
    {
        var registry = connectionString == fixture.ConnectionStringForTests
            ? fixture.Services.GetRequiredService<IMvRegistryStore>()
            : new SqliteMvRegistryStore(connectionString);
        if (rejectEnsureInfrastructure)
        {
            registry = new EnsureRejectingRegistryStore(
                registry,
                Assert.IsAssignableFrom<IMvReadOnlyMvInspector>(registry));
        }

        return new SqliteMvExecutor(
            fixture.EventStoreFactory,
            registry,
            Options.Create(new MvOptions
            {
                ServiceId = MultiProviderFixtureBase.ServiceId,
                InitializationMode = initializationMode,
                SqlStatementPolicy = policy,
                SqlStatementPolicyMode = policyMode,
                SafeWindowMs = 0,
                BatchSize = 100
            }),
            NullLogger<SqliteMvExecutor>.Instance,
            connectionString);
    }

    private async Task<int> ReadSchemaVersionAsync()
    {
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<int>("PRAGMA schema_version;").ConfigureAwait(false);
    }

    private sealed class RecordingPolicy(
        Func<MvSqlStatementContext, MvSqlPolicyDecision> decision) : IMvSqlStatementPolicy
    {
        public List<MvSqlStatementContext> Contexts { get; } = [];

        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(decision(context));
        }
    }

    private sealed class FaultingPolicy : IMvSqlStatementPolicy
    {
        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Phase == MvSqlStatementPhase.Apply)
            {
                throw new InvalidOperationException("deliberate policy fault");
            }

            return ValueTask.FromResult(MvSqlPolicyDecision.Allow());
        }
    }

    private sealed class CancellingPolicy(CancellationTokenSource cancellation) : IMvSqlStatementPolicy
    {
        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Phase == MvSqlStatementPhase.Apply)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.FromResult(MvSqlPolicyDecision.Allow());
        }
    }

    private sealed class EnsureRejectingRegistryStore(
        IMvRegistryStore inner,
        IMvReadOnlyMvInspector inspector) : IMvRegistryStore, IMvReadOnlyMvInspector
    {
        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Verify-only must not enter EnsureInfrastructureAsync.");

        public Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.RegisterAsync(entry, transaction, cancellationToken);

        public Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.UpdatePositionAsync(update, transaction, cancellationToken);

        public Task MarkStreamReceivedAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            string sortableUniqueId,
            DateTimeOffset receivedAt,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.MarkStreamReceivedAsync(serviceId, viewName, viewVersion, sortableUniqueId, receivedAt, transaction, cancellationToken);

        public Task UpdateStatusAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvStatus status,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.UpdateStatusAsync(serviceId, viewName, viewVersion, status, transaction, cancellationToken);

        public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Verify-only must use the dedicated read-only inspector.");

        public Task<IReadOnlyList<MvRegistryEntry>> ReadRegistryEntriesAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            CancellationToken cancellationToken = default) =>
            inspector.ReadRegistryEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);

        public Task<MvActiveEntry?> GetActiveAsync(
            string serviceId,
            string viewName,
            CancellationToken cancellationToken = default) =>
            inner.GetActiveAsync(serviceId, viewName, cancellationToken);

        public Task SetTargetCheckpointAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvCheckpointTruth targetCheckpointTruth,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.SetTargetCheckpointAsync(serviceId, viewName, viewVersion, targetCheckpointTruth, transaction, cancellationToken);

        public Task<MvActivationResult> TryActivateAsync(
            MvActivationRequest request,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, transaction, cancellationToken);

        public Task<MvActivationResult> TryForceReverseAsync(
            MvForcedReverseRequest request,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.TryForceReverseAsync(request, transaction, cancellationToken);

        public Task SetActiveAsync(
            string serviceId,
            string viewName,
            int activeVersion,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            inner.SetActiveAsync(serviceId, viewName, activeVersion, transaction, cancellationToken);

        public Task<MvSchemaVerificationResult> VerifySchemaAsync(
            IReadOnlyList<MvSchemaTableRequirement> requirements,
            CancellationToken cancellationToken = default) =>
            inspector.VerifySchemaAsync(requirements, cancellationToken);
    }

    private sealed class NoInspectorRegistryStore(IMvRegistryStore inner) : IMvRegistryStore
    {
        public int EnsureCalls { get; private set; }
        public int RegisterCalls { get; private set; }
        public int GetEntriesCalls { get; private set; }

        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            return inner.EnsureInfrastructureAsync(cancellationToken);
        }

        public Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? transaction = null, CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            return inner.RegisterAsync(entry, transaction, cancellationToken);
        }

        public Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.UpdatePositionAsync(update, transaction, cancellationToken);

        public Task MarkStreamReceivedAsync(string serviceId, string viewName, int viewVersion, string sortableUniqueId, DateTimeOffset receivedAt, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.MarkStreamReceivedAsync(serviceId, viewName, viewVersion, sortableUniqueId, receivedAt, transaction, cancellationToken);

        public Task UpdateStatusAsync(string serviceId, string viewName, int viewVersion, MvStatus status, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.UpdateStatusAsync(serviceId, viewName, viewVersion, status, transaction, cancellationToken);

        public Task<IReadOnlyList<MvRegistryEntry>> GetEntriesAsync(string serviceId, string viewName, int viewVersion, CancellationToken cancellationToken = default)
        {
            GetEntriesCalls++;
            return inner.GetEntriesAsync(serviceId, viewName, viewVersion, cancellationToken);
        }

        public Task<MvActiveEntry?> GetActiveAsync(string serviceId, string viewName, CancellationToken cancellationToken = default) =>
            inner.GetActiveAsync(serviceId, viewName, cancellationToken);

        public Task SetTargetCheckpointAsync(string serviceId, string viewName, int viewVersion, MvCheckpointTruth targetCheckpointTruth, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.SetTargetCheckpointAsync(serviceId, viewName, viewVersion, targetCheckpointTruth, transaction, cancellationToken);

        public Task<MvActivationResult> TryActivateAsync(MvActivationRequest request, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, transaction, cancellationToken);

        public Task<MvActivationResult> TryForceReverseAsync(MvForcedReverseRequest request, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.TryForceReverseAsync(request, transaction, cancellationToken);

        public Task SetActiveAsync(string serviceId, string viewName, int activeVersion, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            inner.SetActiveAsync(serviceId, viewName, activeVersion, transaction, cancellationToken);
    }

}

internal sealed class CountingApplyHost(IMvApplyHost inner, bool throwOnInitialize = false) : IMvApplyHost
{
    private readonly bool _throwOnInitialize = throwOnInitialize;
    public int InitializeCalls { get; private set; }
    public string ViewName => inner.ViewName;
    public int ViewVersion => inner.ViewVersion;
    public IReadOnlyList<string> LogicalTables => inner.LogicalTables;

    public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
        IMvTableBindings tables,
        CancellationToken ct)
    {
        InitializeCalls += 1;
        if (_throwOnInitialize)
        {
            throw new InvalidOperationException("Verify-only must not invoke projector initialization.");
        }

        return inner.InitializeAsync(tables, ct);
    }

    public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
        SerializableEvent ev,
        IMvTableBindings tables,
        IMvApplyQueryPort queryPort,
        string sortableUniqueId,
        CancellationToken ct) =>
        inner.ApplyEventAsync(ev, tables, queryPort, sortableUniqueId, ct);

    public IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(IMvTableBindings tables) =>
        inner.GetSchemaRequirements(tables);

    public MvSchemaContract? GetSchemaContract(IMvTableBindings tables) =>
        inner.GetSchemaContract(tables);
}

public enum PolicySurfaceKind
{
    Rows,
    Single,
    Scalar,
    Raw,
    Batch
}

internal sealed class PolicySurfaceProjector(PolicySurfaceKind surface, string sql) :
    IMaterializedViewProjector,
    IMvSchemaRequirementsProvider
{
    public string ViewName => "PolicySurface";
    public int ViewVersion => 1;
    public MvTable Records { get; private set; } = default!;

    public IReadOnlyList<MvSchemaTableRequirement> GetSchemaRequirements(
        MvDbType databaseType,
        IMvTableBindings tables) =>
    [
        new MvSchemaTableRequirement(
            "records",
            tables.GetPhysicalName("records"),
            [
                new("id", MvSchemaTypeFamily.String, false),
                new("value", MvSchemaTypeFamily.String, false)
            ],
            ["id"])
    ];

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        Records = ctx.RegisterTable("records");
        await ctx.ExecuteAsync(
                $"CREATE TABLE IF NOT EXISTS {Records.PhysicalName} (id TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default)
    {
        switch (surface)
        {
            case PolicySurfaceKind.Rows:
                await ctx.QueryRowsAsync(sql, cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case PolicySurfaceKind.Single:
                await ctx.QuerySingleOrDefaultRowAsync(sql, cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case PolicySurfaceKind.Scalar:
                await ctx.ExecuteScalarAsync<long>(sql, cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
            case PolicySurfaceKind.Raw:
                _ = ctx.Connection;
                break;
            case PolicySurfaceKind.Batch:
                return
                [
                    new MvSqlStatement($"INSERT INTO {Records.PhysicalName} (id, value) VALUES ('first', 'first');"),
                    new MvSqlStatement($"INSERT INTO {Records.PhysicalName} (id, value) VALUES ('second', 'second');")
                ];
        }

        return
        [
            new MvSqlStatement(
                $"INSERT INTO {Records.PhysicalName} (id, value) VALUES ('applied', 'applied');")
        ];
    }
}

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvVerifyOnlyTests(PostgresMvFixture fixture)
{
    [SkippableFact]
    public Task VerifyOnly_UsesTheCommonContractAgainstTheRealStore() =>
        MvVerifyOnlyAssertions.AssertCommonContractAsync(fixture);
}

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvVerifyOnlyTests(MySqlMvFixture fixture)
{
    [SkippableFact]
    public Task VerifyOnly_UsesTheCommonContractAgainstTheRealStore() =>
        MvVerifyOnlyAssertions.AssertCommonContractAsync(fixture);
}

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvVerifyOnlyTests(SqlServerMvFixture fixture)
{
    [SkippableFact]
    public Task VerifyOnly_UsesTheCommonContractAgainstTheRealStore() =>
        MvVerifyOnlyAssertions.AssertCommonContractAsync(fixture);
}

internal static class MvVerifyOnlyAssertions
{
    public static async Task AssertCommonContractAsync(MultiProviderFixtureBase fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Provider fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);

        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
        await fixture.Executor.InitializeAsync(host).ConfigureAwait(false);
        var registryStore = fixture.Services.GetRequiredService<IMvRegistryStore>();
        var entriesBefore = await registryStore.GetEntriesAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName,
                projector.ViewVersion)
            .ConfigureAwait(false);

        var policy = new RecordingPolicy(_ => MvSqlPolicyDecision.Allow());
        var catalogCommands = new List<string>();
        var verifyExecutor = CreateVerifyExecutor(fixture, policy, catalogCommands.Add);
        var verifyHost = new CountingApplyHost(
            new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests));
        await verifyExecutor.InitializeAsync(
                verifyHost)
            .ConfigureAwait(false);

        Assert.Equal(0, verifyHost.InitializeCalls);
        Assert.Empty(policy.Contexts);
        Assert.NotEmpty(catalogCommands);
        Assert.All(catalogCommands, command =>
        {
            var normalized = command.TrimStart();
            Assert.StartsWith("SELECT", normalized, StringComparison.OrdinalIgnoreCase);
            Assert.False(
                Regex.IsMatch(
                    normalized,
                    @"\b(?:CREATE|ALTER|DROP|INSERT|UPDATE|DELETE)\s",
                    RegexOptions.IgnoreCase),
                normalized);
        });
        var entriesAfter = await registryStore.GetEntriesAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName,
                projector.ViewVersion)
            .ConfigureAwait(false);
        Assert.Equal(entriesBefore, entriesAfter);
    }

    private static IMvExecutor CreateVerifyExecutor(
        MultiProviderFixtureBase fixture,
        IMvSqlStatementPolicy policy,
        Action<string>? catalogCommandRecorder)
    {
        var options = Options.Create(new MvOptions
        {
            ServiceId = MultiProviderFixtureBase.ServiceId,
            InitializationMode = MvInitializationMode.VerifyOnly,
            SqlStatementPolicy = policy,
            SafeWindowMs = 0,
            BatchSize = 100
        });
        var connectionString = fixture.ConnectionStringForTests;
        return fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => new PostgresMvExecutor(
                fixture.EventStoreFactory,
                new PostgresMvRegistryStore(connectionString, catalogCommandRecorder),
                options,
                NullLogger<PostgresMvExecutor>.Instance,
                connectionString),
            MvDbType.MySql => new MySqlMvExecutor(
                fixture.EventStoreFactory,
                new MySqlMvRegistryStore(connectionString, catalogCommandRecorder),
                options,
                NullLogger<MySqlMvExecutor>.Instance,
                connectionString),
            MvDbType.SqlServer => new SqlServerMvExecutor(
                fixture.EventStoreFactory,
                new SqlServerMvRegistryStore(connectionString, catalogCommandRecorder),
                options,
                NullLogger<SqlServerMvExecutor>.Instance,
                connectionString),
            MvDbType.Sqlite => new SqliteMvExecutor(
                fixture.EventStoreFactory,
                new SqliteMvRegistryStore(connectionString, catalogCommandRecorder),
                options,
                NullLogger<SqliteMvExecutor>.Instance,
                connectionString),
            _ => throw new NotSupportedException($"Provider '{fixture.DatabaseTypeForTests}' is not supported.")
        };
    }

    private sealed class RecordingPolicy(
        Func<MvSqlStatementContext, MvSqlPolicyDecision> decision) : IMvSqlStatementPolicy
    {
        public List<MvSqlStatementContext> Contexts { get; } = [];

        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(decision(context));
        }
    }
}
