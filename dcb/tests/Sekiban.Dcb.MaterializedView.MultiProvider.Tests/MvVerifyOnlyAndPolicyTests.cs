using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Orleans;
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
        var policy = new RecordingPolicy(_ => MvSqlPolicyDecision.Allow());
        var readOnlyConnections = new List<string>();
        var verifyExecutor = CreateExecutor(
            fixture.ConnectionStringForTests,
            MvInitializationMode.VerifyOnly,
            policy,
            rejectEnsureInfrastructure: true,
            readOnlyConnectionRecorder: readOnlyConnections.Add);

        var verifyHost = new CountingApplyHost(CreateHost(projector), throwOnInitialize: true);
        await verifyExecutor.InitializeAsync(verifyHost).ConfigureAwait(false);

        var schemaVersionAfter = await ReadSchemaVersionAsync().ConfigureAwait(false);
        Assert.Equal(schemaVersionBefore, schemaVersionAfter);
        Assert.Equal(0, verifyHost.InitializeCalls);
        Assert.Empty(policy.Contexts);
        Assert.Contains("sqlite:Mode=ReadOnly", readOnlyConnections);
    }

    [SkippableFact]
    public async Task VerifyOnly_CompatibleOperationBoundaries_NeverReachRegistryMutation()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQLite fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var host = CreateHost(projector);
        await fixture.Executor.InitializeAsync(host).ConfigureAwait(false);

        var readOnlyConnections = new List<string>();
        var realStore = new SqliteMvRegistryStore(
            fixture.ConnectionStringForTests,
            catalogCommandRecorder: null,
            readOnlyConnectionRecorder: readOnlyConnections.Add);
        await realStore.SetTargetCheckpointAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName,
                projector.ViewVersion,
                MvCheckpointTruth.KnownZero(MvCheckpointProvenance.AuthoritativeTargetCapture()))
            .ConfigureAwait(false);
        await realStore.UpdatePositionAsync(
                new MvPositionUpdate(
                    MultiProviderFixtureBase.ServiceId,
                    projector.ViewName,
                    projector.ViewVersion,
                    SortableUniqueId.MinValue.Value,
                    MvApplySource.CatchUp,
                    AppliedEventVersionDelta: 0)
                {
                    CheckpointTruth = MvCheckpointTruth.KnownZero()
                })
            .ConfigureAwait(false);
        await realStore.UpdateStatusAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName,
                projector.ViewVersion,
                MvStatus.Ready)
            .ConfigureAwait(false);
        var guardedRegistry = new EnsureRejectingRegistryStore(
            Assert.IsAssignableFrom<IMvReadOnlyMvInspector>(realStore));
        var executor = new SqliteMvExecutor(
            fixture.EventStoreFactory,
            guardedRegistry,
            Options.Create(new MvOptions
            {
                ServiceId = MultiProviderFixtureBase.ServiceId,
                InitializationMode = MvInitializationMode.VerifyOnly,
                SafeWindowMs = 0,
                BatchSize = 100
            }),
            NullLogger<SqliteMvExecutor>.Instance,
            fixture.ConnectionStringForTests);

        await executor.InitializeAsync(new CountingApplyHost(host, throwOnInitialize: true)).ConfigureAwait(false);
        var capture = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => executor.CaptureTargetCheckpointAsync(host)).ConfigureAwait(false);
        var catchUp = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => executor.CatchUpOnceAsync(host)).ConfigureAwait(false);
        var activation = await executor.TryActivateAsync(host).ConfigureAwait(false);
        var apply = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => executor.ApplySerializableEventsAsync(host, [CreatePolicyEvent()])).ConfigureAwait(false);
        var coordinator = new MvGenerationCoordinator(
            executor,
            guardedRegistry,
            Options.Create(new MvOptions
            {
                ServiceId = MultiProviderFixtureBase.ServiceId,
                InitializationMode = MvInitializationMode.VerifyOnly
            }));
        var prepare = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => coordinator.PrepareGenerationAsync(host));
        var switchResult = await coordinator.SwitchAsync(host).ConfigureAwait(false);
        var forcedReverse = await coordinator.ForceReverseAsync(
                host,
                expectedActiveVersion: projector.ViewVersion + 1,
                expectedActiveGeneration: 0,
                reason: "verify-only mutation probe")
            .ConfigureAwait(false);

        Assert.Equal(MvTransition.CaptureTargetCheckpoint, capture.Transition);
        Assert.Equal(MvTransition.CatchUp, catchUp.Transition);
        Assert.Equal(MvTransition.Apply, apply.Transition);
        Assert.Equal(MvTransition.CaptureTargetCheckpoint, prepare.Transition);
        Assert.Equal(MvActivationFailureReason.TransitionNotAllowed, activation.FailureReason);
        Assert.Equal(MvActivationFailureReason.TransitionNotAllowed, switchResult.FailureReason);
        Assert.Equal(MvActivationFailureReason.TransitionNotAllowed, forcedReverse.FailureReason);
        Assert.Equal(0, guardedRegistry.EnsureCalls);
        Assert.Equal(0, guardedRegistry.RegisterCalls);
        Assert.Equal(0, guardedRegistry.TargetCheckpointCalls);
        Assert.Equal(0, guardedRegistry.PositionCalls);
        Assert.Equal(0, guardedRegistry.StatusCalls);
        Assert.Equal(0, guardedRegistry.ActivationCalls);
        Assert.Equal(0, guardedRegistry.ActivePointerCalls);
        Assert.Contains("sqlite:Mode=ReadOnly", readOnlyConnections);

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
    public async Task VerifyOnly_MutatingBoundariesRefuseBeforeSchemaOrRegistryAccess()
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
                "target-capture" => await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
                    () => verifyExecutor.CaptureTargetCheckpointAsync(host)).ConfigureAwait(false),
                "catch-up" => await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
                    async () => await verifyExecutor.CatchUpOnceAsync(host).ConfigureAwait(false)).ConfigureAwait(false),
                _ => await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
                    () => verifyExecutor.ApplySerializableEventsAsync(host, [])).ConfigureAwait(false)
            };

            Assert.Equal(MvTransitionNotAllowedReason.VerifyOnly, exception.Reason);
            Assert.Equal(0, host.InitializeCalls);
            await using var checkConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
            Assert.Equal(0, await checkConnection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sekiban_mv_registry;"));
        }
    }

    [SkippableFact]
    public async Task VerifyOnly_MutatingBoundariesRefuseEvenWhenFrameworkSchemaIsMissing()
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
                "target-capture" => await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
                    () => verifyExecutor.CaptureTargetCheckpointAsync(host)).ConfigureAwait(false),
                "catch-up" => await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
                    async () => await verifyExecutor.CatchUpOnceAsync(host).ConfigureAwait(false)).ConfigureAwait(false),
                _ => await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
                    () => verifyExecutor.ApplySerializableEventsAsync(host, [])).ConfigureAwait(false)
            };

            Assert.Equal(MvTransitionNotAllowedReason.VerifyOnly, exception.Reason);
            Assert.Equal(0, host.InitializeCalls);
            await using var checkConnection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
            Assert.Equal(
                0,
                await checkConnection.ExecuteScalarAsync<long>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE name LIKE 'sekiban_mv_%';"));
        }
    }

    [Fact]
    public async Task VerifyOnly_PublicRefreshRefusesBeforeItCanReachGrainDependencies()
    {
        var grain = new MaterializedViewGrain(
            hostFactory: null!,
            executor: null!,
            registryStore: null!,
            subscriptionResolver: null!,
            options: Options.Create(new MvOptions
            {
                ServiceId = "g34-refresh",
                InitializationMode = MvInitializationMode.VerifyOnly
            }),
            logger: NullLogger<MaterializedViewGrain>.Instance);
        SetPrivateField(grain, "_grainKey", "g34-refresh|RefreshView|1");
        SetPrivateField(grain, "_serviceId", "g34-refresh");
        SetPrivateField(grain, "_viewName", "RefreshView");
        SetPrivateField(grain, "_viewVersion", 1);

        var refusal = await Assert.ThrowsAsync<MvTransitionNotAllowedException>(
            () => grain.RefreshAsync());

        Assert.Equal(MvTransition.Refresh, refusal.Transition);
        Assert.Equal(MvTransitionNotAllowedReason.VerifyOnly, refusal.Reason);
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
        Assert.Equal(MvSqlStatementOrigin.ProjectorInitialize, policy.Contexts[0].Origin);
        Assert.Equal(MvDbType.Sqlite, policy.Contexts[0].DatabaseType);
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
                ? MvSqlPolicyDecision.Reject("Apply is not approved for this host.", "apply-deny-rule")
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
        Assert.Equal(MvSqlStatementOrigin.ProjectorApply, exception.Failure.Origin);
        Assert.Equal(MvDbType.Sqlite, exception.Failure.DatabaseType);
        Assert.Equal("apply-deny-rule", exception.Failure.RuleId);
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
        Assert.Equal(MvSqlStatementOrigin.ProjectorQuery, queryContext.Origin);
        Assert.Equal(MvDbType.Sqlite, queryContext.DatabaseType);
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
        Assert.Equal(MvSqlStatementOrigin.ProjectorQuery, exception.Failure.Origin);
        Assert.Equal(MvDbType.Sqlite, exception.Failure.DatabaseType);
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

    private static void SetPrivateField<T>(MaterializedViewGrain grain, string fieldName, T value)
    {
        var field = typeof(MaterializedViewGrain).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(grain, value);
    }

    private NativeMvApplyHost CreateHost(CrossProviderWeatherForecastMvV1 projector) =>
        new(projector, fixture.DomainTypes.EventTypes, MvDbType.Sqlite);

    private SqliteMvExecutor CreateExecutor(
        string connectionString,
        MvInitializationMode initializationMode,
        IMvSqlStatementPolicy? policy,
        bool rejectEnsureInfrastructure = false,
        MvSqlStatementPolicyMode policyMode = MvSqlStatementPolicyMode.Legacy,
        Action<string>? readOnlyConnectionRecorder = null)
    {
        var registry = connectionString == fixture.ConnectionStringForTests && readOnlyConnectionRecorder is null
            ? fixture.Services.GetRequiredService<IMvRegistryStore>()
            : new SqliteMvRegistryStore(connectionString, null, readOnlyConnectionRecorder);
        if (rejectEnsureInfrastructure)
        {
            registry = new EnsureRejectingRegistryStore(
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
        IMvReadOnlyMvInspector inspector) : IMvRegistryStore, IMvReadOnlyMvInspector
    {
        public int EnsureCalls;
        public int RegisterCalls;
        public int PositionCalls;
        public int StatusCalls;
        public int TargetCheckpointCalls;
        public int ActivationCalls;
        public int ActivePointerCalls;

        public Task EnsureInfrastructureAsync(CancellationToken cancellationToken = default) =>
            FailMutation(ref EnsureCalls, "EnsureInfrastructureAsync");

        public Task RegisterAsync(MvRegistryEntry entry, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            FailMutation(ref RegisterCalls, "RegisterAsync");

        public Task UpdatePositionAsync(MvPositionUpdate update, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) =>
            FailMutation(ref PositionCalls, "UpdatePositionAsync");

        public Task MarkStreamReceivedAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            string sortableUniqueId,
            DateTimeOffset receivedAt,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            FailMutation(ref PositionCalls, "MarkStreamReceivedAsync");

        public Task UpdateStatusAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvStatus status,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            FailMutation(ref StatusCalls, "UpdateStatusAsync");

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
            throw new InvalidOperationException("Verify-only must use the dedicated read-only active-pointer inspector.");

        public Task<MvActiveEntry?> ReadActiveAsync(
            string serviceId,
            string viewName,
            CancellationToken cancellationToken = default) =>
            inspector.ReadActiveAsync(serviceId, viewName, cancellationToken);

        public Task SetTargetCheckpointAsync(
            string serviceId,
            string viewName,
            int viewVersion,
            MvCheckpointTruth targetCheckpointTruth,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            FailMutation(ref TargetCheckpointCalls, "SetTargetCheckpointAsync");

        public Task<MvActivationResult> TryActivateAsync(
            MvActivationRequest request,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            FailActivation(ref ActivationCalls, "TryActivateAsync");

        public Task<MvActivationResult> TryForceReverseAsync(
            MvForcedReverseRequest request,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            FailActivation(ref ActivationCalls, "TryForceReverseAsync");

        public Task SetActiveAsync(
            string serviceId,
            string viewName,
            int activeVersion,
            IDbTransaction? transaction = null,
            CancellationToken cancellationToken = default) =>
            FailMutation(ref ActivePointerCalls, "SetActiveAsync");

        public Task<MvSchemaVerificationResult> VerifySchemaAsync(
            IReadOnlyList<MvSchemaTableRequirement> requirements,
            CancellationToken cancellationToken = default) =>
            inspector.VerifySchemaAsync(requirements, cancellationToken);

        private static Task FailMutation(ref int counter, string operation)
        {
            counter++;
            throw new InvalidOperationException($"Verify-only registry mutation reached: {operation}.");
        }

        private static Task<MvActivationResult> FailActivation(ref int counter, string operation)
        {
            counter++;
            throw new InvalidOperationException($"Verify-only registry mutation reached: {operation}.");
        }
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

/// <summary>
///     Adds a projector-emitted DDL statement after the real projector INSERT. Mode 2 must authorize the complete
///     batch before either statement reaches the provider, so the second statement is a direct wrong-ordering probe.
/// </summary>
internal sealed class DdlAppendingApplyHost(IMvApplyHost inner, string deniedDdl) : IMvApplyHost
{
    public string ViewName => inner.ViewName;
    public int ViewVersion => inner.ViewVersion;
    public IReadOnlyList<string> LogicalTables => inner.LogicalTables;

    public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
        IMvTableBindings tables,
        CancellationToken ct) =>
        inner.InitializeAsync(tables, ct);

    public async Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
        SerializableEvent ev,
        IMvTableBindings tables,
        IMvApplyQueryPort queryPort,
        string sortableUniqueId,
        CancellationToken ct)
    {
        var statements = await inner.ApplyEventAsync(ev, tables, queryPort, sortableUniqueId, ct).ConfigureAwait(false);
        return [.. statements, new MvSqlStatementDto(deniedDdl, [])];
    }

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

    [SkippableFact]
    public Task SchemaContractMatrix_UsesTheProductionInspector() =>
        MvSchemaMatrixAssertions.AssertAsync(fixture);

    [SkippableFact]
    public async Task VerifyAndExecute_UsesDdlDeniedRoleForProductionDmlAndFailsAtomicallyOnPolicyReject()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "PostgreSQL fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);

        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, MvDbType.Postgres);
        await fixture.Executor.InitializeAsync(host).ConfigureAwait(false);

        var roleName = $"g34_exec_{Guid.NewGuid():N}";
        var rolePassword = Guid.NewGuid().ToString("N");
        try
        {
            await using var ownerConnection = new NpgsqlConnection(fixture.ConnectionStringForTests);
            await ownerConnection.OpenAsync().ConfigureAwait(false);
            var ownerRole = await ownerConnection.ExecuteScalarAsync<string>("SELECT current_user;").ConfigureAwait(false);
            await ownerConnection.ExecuteAsync($"""
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                GRANT CREATE ON SCHEMA public TO {ownerRole};
                CREATE ROLE {roleName} LOGIN PASSWORD '{rolePassword}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
                GRANT CONNECT ON DATABASE sekiban_mv_test TO {roleName};
                GRANT USAGE ON SCHEMA public TO {roleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {roleName};
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {roleName};
                REVOKE CREATE ON SCHEMA public FROM {roleName};
                """).ConfigureAwait(false);

            var restrictedBuilder = new NpgsqlConnectionStringBuilder(fixture.ConnectionStringForTests)
            {
                Username = roleName,
                Password = rolePassword,
                Pooling = false
            };
            var restrictedConnectionString = restrictedBuilder.ConnectionString;
            await using (var restrictedConnection = new NpgsqlConnection(restrictedConnectionString))
            {
                await restrictedConnection.OpenAsync().ConfigureAwait(false);
                var create = await Assert.ThrowsAsync<PostgresException>(
                    () => restrictedConnection.ExecuteAsync($"CREATE TABLE g34_create_probe_{Guid.NewGuid():N} (id INT);")).ConfigureAwait(false);
                var alter = await Assert.ThrowsAsync<PostgresException>(
                    () => restrictedConnection.ExecuteAsync($"ALTER TABLE {projector.Forecasts.PhysicalName} ADD COLUMN g34_alter_probe INT;")).ConfigureAwait(false);
                var drop = await Assert.ThrowsAsync<PostgresException>(
                    () => restrictedConnection.ExecuteAsync($"DROP TABLE {projector.Forecasts.PhysicalName};")).ConfigureAwait(false);
                Assert.Equal("42501", create.SqlState);
                Assert.Equal("42501", alter.SqlState);
                Assert.Equal("42501", drop.SqlState);
            }

            var readRows = () => ownerConnection.ExecuteScalar<long>($"SELECT COUNT(*) FROM {projector.Forecasts.PhysicalName};");
            var ownerRegistry = fixture.Services.GetRequiredService<IMvRegistryStore>();
            async Task<string[]> SnapshotRegistryAsync()
            {
                var rows = await ownerConnection.QueryAsync<string>(
                        """
                        SELECT row_to_json(snapshot)::text
                        FROM (
                            SELECT *
                            FROM sekiban_mv_registry
                            WHERE service_id = @ServiceId
                              AND view_name = @ViewName
                              AND view_version = @ViewVersion
                            ORDER BY logical_table
                        ) AS snapshot;
                        """,
                        new
                        {
                            ServiceId = MultiProviderFixtureBase.ServiceId,
                            projector.ViewName,
                            projector.ViewVersion
                        })
                    .ConfigureAwait(false);
                return rows.ToArray();
            }

            async Task<string[]> SnapshotActiveAsync()
            {
                var rows = await ownerConnection.QueryAsync<string>(
                        """
                        SELECT row_to_json(snapshot)::text
                        FROM (
                            SELECT *
                            FROM sekiban_mv_active
                            WHERE service_id = @ServiceId
                              AND view_name = @ViewName
                        ) AS snapshot;
                        """,
                        new
                        {
                            ServiceId = MultiProviderFixtureBase.ServiceId,
                            projector.ViewName
                        })
                    .ConfigureAwait(false);
                return rows.ToArray();
            }

            async Task<string[]> SnapshotProjectionRowsAsync()
            {
                var rows = await ownerConnection.QueryAsync<string>(
                        $"""
                        SELECT row_to_json(snapshot)::text
                        FROM (
                            SELECT *
                            FROM {projector.Forecasts.PhysicalName}
                            ORDER BY forecast_id
                        ) AS snapshot;
                        """)
                    .ConfigureAwait(false);
                return rows.ToArray();
            }

            async Task<string[]> SnapshotSchemaFingerprintAsync()
            {
                var rows = await ownerConnection.QueryAsync<string>(
                        """
                        WITH schema_objects AS (
                            SELECT
                                'relation' AS object_kind,
                                schema_namespace.nspname AS schema_name,
                                relation.relname AS object_name,
                                json_build_object(
                                    'kind', relation.relkind,
                                    'persistence', relation.relpersistence,
                                    'view_definition', CASE
                                        WHEN relation.relkind IN ('v', 'm') THEN pg_get_viewdef(relation.oid, true)
                                        ELSE NULL
                                    END)::text AS definition
                            FROM pg_class AS relation
                            INNER JOIN pg_namespace AS schema_namespace ON schema_namespace.oid = relation.relnamespace
                            WHERE schema_namespace.nspname = current_schema()
                              AND relation.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')

                            UNION ALL

                            SELECT
                                'column',
                                schema_namespace.nspname,
                                relation.relname || '.' || attribute.attname,
                                json_build_object(
                                    'position', attribute.attnum,
                                    'type', format_type(attribute.atttypid, attribute.atttypmod),
                                    'not_null', attribute.attnotnull,
                                    'default', pg_get_expr(default_value.adbin, default_value.adrelid),
                                    'identity', attribute.attidentity,
                                    'generated', attribute.attgenerated)::text
                            FROM pg_attribute AS attribute
                            INNER JOIN pg_class AS relation ON relation.oid = attribute.attrelid
                            INNER JOIN pg_namespace AS schema_namespace ON schema_namespace.oid = relation.relnamespace
                            LEFT JOIN pg_attrdef AS default_value
                                ON default_value.adrelid = attribute.attrelid
                               AND default_value.adnum = attribute.attnum
                            WHERE schema_namespace.nspname = current_schema()
                              AND relation.relkind IN ('r', 'p', 'v', 'm', 'S', 'f')
                              AND attribute.attnum > 0
                              AND NOT attribute.attisdropped

                            UNION ALL

                            SELECT
                                'constraint',
                                schema_namespace.nspname,
                                relation.relname || '.' || constraint_item.conname,
                                pg_get_constraintdef(constraint_item.oid, true)
                            FROM pg_constraint AS constraint_item
                            INNER JOIN pg_class AS relation ON relation.oid = constraint_item.conrelid
                            INNER JOIN pg_namespace AS schema_namespace ON schema_namespace.oid = relation.relnamespace
                            WHERE schema_namespace.nspname = current_schema()

                            UNION ALL

                            SELECT
                                'index',
                                schema_namespace.nspname,
                                relation.relname || '.' || index_relation.relname,
                                pg_get_indexdef(index_relation.oid)
                            FROM pg_index AS index_item
                            INNER JOIN pg_class AS relation ON relation.oid = index_item.indrelid
                            INNER JOIN pg_class AS index_relation ON index_relation.oid = index_item.indexrelid
                            INNER JOIN pg_namespace AS schema_namespace ON schema_namespace.oid = relation.relnamespace
                            WHERE schema_namespace.nspname = current_schema()
                        )
                        SELECT row_to_json(fingerprint)::text
                        FROM (
                            SELECT object_kind, schema_name, object_name, definition
                            FROM schema_objects
                            ORDER BY object_kind, schema_name, object_name, definition
                        ) AS fingerprint;
                        """)
                    .ConfigureAwait(false);
                return rows.ToArray();
            }

            var allowPolicy = new ObservingPolicy(readRows, _ => MvSqlPolicyDecision.Allow());
            var allowExecutionAudit = new ExecutionAudit();
            var allowExecutor = CreateVerifyAndExecuteExecutor(restrictedConnectionString, allowPolicy, allowExecutionAudit);
            await allowExecutor.InitializeAsync(host).ConfigureAwait(false);
            allowExecutionAudit.Reset();
            var schemaBeforeAllow = await SnapshotSchemaFingerprintAsync().ConfigureAwait(false);
            Assert.NotEmpty(schemaBeforeAllow);

            var emptyApply = await allowExecutor.ApplySerializableEventsAsync(host, []).ConfigureAwait(false);
            Assert.Equal(0, emptyApply);

            var applied = await allowExecutor.ApplySerializableEventsAsync(
                    host,
                    [CreateEvent(fixture, DateTime.UtcNow.AddMinutes(-2))])
                .ConfigureAwait(false);
            Assert.Equal(1, applied);
            Assert.Single(allowPolicy.ApplyContexts);
            Assert.Equal(MvSqlStatementPhase.Apply, allowPolicy.ApplyContexts[0].Phase);
            Assert.All(allowPolicy.ObservedRowsBeforeApply, count => Assert.Equal(0, count));
            Assert.DoesNotContain(
                allowPolicy.ApplyContexts,
                context => IsDdlStatement(context.Sql));
            Assert.Equal(1, readRows());

            var beforeLifecycle = await ownerRegistry.GetEntriesAsync(
                    MultiProviderFixtureBase.ServiceId,
                    projector.ViewName,
                    projector.ViewVersion)
                .ConfigureAwait(false);
            var sourceEvent = CreateEvent(fixture, DateTime.UtcNow.AddSeconds(-30));
            var writeSource = await fixture.EventStore.WriteSerializableEventsAsync([sourceEvent]).ConfigureAwait(false);
            Assert.True(writeSource.IsSuccess);
            var target = await allowExecutor.CaptureTargetCheckpointAsync(host).ConfigureAwait(false);
            Assert.True(target.IsKnown);
            var firstCatchUp = await allowExecutor.CatchUpOnceAsync(host).ConfigureAwait(false);
            Assert.Equal(1, firstCatchUp.AppliedEvents);
            var afterFirstCatchUp = await ownerRegistry.GetEntriesAsync(
                    MultiProviderFixtureBase.ServiceId,
                    projector.ViewName,
                    projector.ViewVersion)
                .ConfigureAwait(false);
            Assert.Equal(
                beforeLifecycle.Single().AppliedEventVersion + 1,
                afterFirstCatchUp.Single().AppliedEventVersion);
            _ = await allowExecutor.CatchUpOnceAsync(host).ConfigureAwait(false);
            var activeAfterFirstCas = await ownerRegistry.GetActiveAsync(
                    MultiProviderFixtureBase.ServiceId,
                    projector.ViewName)
                .ConfigureAwait(false);
            Assert.NotNull(activeAfterFirstCas);
            Assert.Equal(projector.ViewVersion, activeAfterFirstCas.ActiveVersion);
            var registryBeforeReplay = await SnapshotRegistryAsync().ConfigureAwait(false);
            var activeBeforeReplay = await SnapshotActiveAsync().ConfigureAwait(false);
            var replay = await allowExecutor.CatchUpOnceAsync(host).ConfigureAwait(false);
            Assert.Equal(0, replay.AppliedEvents);
            Assert.Equal(registryBeforeReplay, await SnapshotRegistryAsync().ConfigureAwait(false));
            Assert.Equal(activeBeforeReplay, await SnapshotActiveAsync().ConfigureAwait(false));
            Assert.Equal(2, readRows());
            Assert.Equal(schemaBeforeAllow, await SnapshotSchemaFingerprintAsync().ConfigureAwait(false));
            Assert.Equal(0, allowPolicy.ApplyContexts.Count(context => IsDdlStatement(context.Sql)));
            Assert.Equal(0, allowExecutionAudit.ProjectorCommandExecutionAttempts.Count(IsDdlStatement));
            Assert.Equal(2, allowExecutionAudit.ProjectorCommandExecutionAttempts.Count);
            Assert.Equal(2, allowExecutionAudit.TransactionCommitCount);

            var registryBeforeReject = await SnapshotRegistryAsync().ConfigureAwait(false);
            var activeBeforeReject = await SnapshotActiveAsync().ConfigureAwait(false);
            var projectionRowsBeforeReject = await SnapshotProjectionRowsAsync().ConfigureAwait(false);
            var schemaBeforeReject = await SnapshotSchemaFingerprintAsync().ConfigureAwait(false);
            var rowsBeforeReject = projectionRowsBeforeReject.LongLength;
            var denySecondPolicy = new ObservingPolicy(
                readRows,
                context => context.StatementIndex == 1
                    ? MvSqlPolicyDecision.Reject("second statement is intentionally denied", "g34-deny-second")
                    : MvSqlPolicyDecision.Allow());
            var denySecondExecutionAudit = new ExecutionAudit();
            var rejectExecutor = CreateVerifyAndExecuteExecutor(
                restrictedConnectionString,
                denySecondPolicy,
                denySecondExecutionAudit);
            await rejectExecutor.InitializeAsync(host).ConfigureAwait(false);
            denySecondExecutionAudit.Reset();

            var rejection = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
                () => rejectExecutor.ApplySerializableEventsAsync(
                    host,
                    [
                        CreateEvent(fixture, DateTime.UtcNow.AddMinutes(-1)),
                        CreateEvent(fixture, DateTime.UtcNow)
                    ])).ConfigureAwait(false);

            Assert.Equal(MvSqlPolicyFailureReason.Denied, rejection.Failure.FailureReason);
            Assert.Equal(rowsBeforeReject, readRows());
            Assert.All(denySecondPolicy.ObservedRowsBeforeApply, count => Assert.Equal(rowsBeforeReject, count));
            Assert.Equal(2, denySecondPolicy.ApplyContexts.Count);
            Assert.Equal([0, 1], denySecondPolicy.ApplyContexts.Select(context => context.StatementIndex));
            Assert.Equal(projectionRowsBeforeReject, await SnapshotProjectionRowsAsync().ConfigureAwait(false));
            Assert.Equal(registryBeforeReject, await SnapshotRegistryAsync().ConfigureAwait(false));
            Assert.Equal(activeBeforeReject, await SnapshotActiveAsync().ConfigureAwait(false));
            Assert.Equal(schemaBeforeReject, await SnapshotSchemaFingerprintAsync().ConfigureAwait(false));
            Assert.Empty(denySecondExecutionAudit.ProjectorCommandExecutionAttempts);
            Assert.Equal(0, denySecondExecutionAudit.TransactionCommitCount);

            var registryBeforeDenyAll = await SnapshotRegistryAsync().ConfigureAwait(false);
            var activeBeforeDenyAll = await SnapshotActiveAsync().ConfigureAwait(false);
            var projectionRowsBeforeDenyAll = await SnapshotProjectionRowsAsync().ConfigureAwait(false);
            var schemaBeforeDenyAll = await SnapshotSchemaFingerprintAsync().ConfigureAwait(false);
            var denyAllPolicy = new ObservingPolicy(
                readRows,
                _ => MvSqlPolicyDecision.Reject("all mode-2 projector commands are intentionally denied", "g34-deny-all"));
            var denyAllExecutionAudit = new ExecutionAudit();
            var denyAllExecutor = CreateVerifyAndExecuteExecutor(
                restrictedConnectionString,
                denyAllPolicy,
                denyAllExecutionAudit);
            await denyAllExecutor.InitializeAsync(host).ConfigureAwait(false);
            denyAllExecutionAudit.Reset();

            var denyAllRejection = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
                () => denyAllExecutor.ApplySerializableEventsAsync(
                    host,
                    [CreateEvent(fixture, DateTime.UtcNow.AddMinutes(1))])).ConfigureAwait(false);

            Assert.Equal(MvSqlPolicyFailureReason.Denied, denyAllRejection.Failure.FailureReason);
            Assert.Single(denyAllPolicy.ApplyContexts);
            Assert.Equal(0, denyAllPolicy.ApplyContexts[0].StatementIndex);
            Assert.Equal(1, denyAllPolicy.ApplyContexts[0].BatchSize);
            Assert.All(denyAllPolicy.ObservedRowsBeforeApply, count => Assert.Equal(rowsBeforeReject, count));
            Assert.Empty(denyAllExecutionAudit.ProjectorCommandExecutionAttempts);
            Assert.Equal(0, denyAllExecutionAudit.TransactionCommitCount);
            Assert.Equal(projectionRowsBeforeDenyAll, await SnapshotProjectionRowsAsync().ConfigureAwait(false));
            Assert.Equal(registryBeforeDenyAll, await SnapshotRegistryAsync().ConfigureAwait(false));
            Assert.Equal(activeBeforeDenyAll, await SnapshotActiveAsync().ConfigureAwait(false));
            Assert.Equal(schemaBeforeDenyAll, await SnapshotSchemaFingerprintAsync().ConfigureAwait(false));

            var registryBeforeDdlDeny = await SnapshotRegistryAsync().ConfigureAwait(false);
            var activeBeforeDdlDeny = await SnapshotActiveAsync().ConfigureAwait(false);
            var projectionRowsBeforeDdlDeny = await SnapshotProjectionRowsAsync().ConfigureAwait(false);
            var schemaBeforeDdlDeny = await SnapshotSchemaFingerprintAsync().ConfigureAwait(false);
            var deniedDdlTable = $"g34_policy_denied_{Guid.NewGuid():N}";
            var ddlDenyHost = new DdlAppendingApplyHost(host, $"CREATE TABLE {deniedDdlTable} (id INT);");
            var ddlDenyPolicy = new ObservingPolicy(
                readRows,
                context => IsDdlStatement(context.Sql)
                    ? MvSqlPolicyDecision.Reject("DDL is intentionally denied before provider execution", "g34-deny-ddl")
                    : MvSqlPolicyDecision.Allow());
            var ddlDenyExecutionAudit = new ExecutionAudit();
            var ddlDenyExecutor = CreateVerifyAndExecuteExecutor(
                restrictedConnectionString,
                ddlDenyPolicy,
                ddlDenyExecutionAudit);
            await ddlDenyExecutor.InitializeAsync(ddlDenyHost).ConfigureAwait(false);
            ddlDenyExecutionAudit.Reset();

            var ddlDenyRejection = await Assert.ThrowsAsync<MvSqlPolicyRejectedException>(
                () => ddlDenyExecutor.ApplySerializableEventsAsync(
                    ddlDenyHost,
                    [CreateEvent(fixture, DateTime.UtcNow.AddMinutes(2))])).ConfigureAwait(false);

            Assert.Equal(MvSqlPolicyFailureReason.Denied, ddlDenyRejection.Failure.FailureReason);
            Assert.Equal(2, ddlDenyPolicy.ApplyContexts.Count);
            Assert.Equal([0, 1], ddlDenyPolicy.ApplyContexts.Select(context => context.StatementIndex));
            Assert.False(IsDdlStatement(ddlDenyPolicy.ApplyContexts[0].Sql));
            Assert.True(IsDdlStatement(ddlDenyPolicy.ApplyContexts[1].Sql));
            Assert.All(ddlDenyPolicy.ObservedRowsBeforeApply, count => Assert.Equal(rowsBeforeReject, count));
            Assert.Empty(ddlDenyExecutionAudit.ProjectorCommandExecutionAttempts);
            Assert.Equal(0, ddlDenyExecutionAudit.TransactionCommitCount);
            Assert.Equal(projectionRowsBeforeDdlDeny, await SnapshotProjectionRowsAsync().ConfigureAwait(false));
            Assert.Equal(registryBeforeDdlDeny, await SnapshotRegistryAsync().ConfigureAwait(false));
            Assert.Equal(activeBeforeDdlDeny, await SnapshotActiveAsync().ConfigureAwait(false));
            Assert.Equal(schemaBeforeDdlDeny, await SnapshotSchemaFingerprintAsync().ConfigureAwait(false));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanupConnection = new NpgsqlConnection(fixture.ConnectionStringForTests);
            await cleanupConnection.OpenAsync().ConfigureAwait(false);
            await cleanupConnection.ExecuteAsync($"DROP OWNED BY {roleName};").ConfigureAwait(false);
            await cleanupConnection.ExecuteAsync($"DROP ROLE IF EXISTS {roleName};").ConfigureAwait(false);
            await cleanupConnection.ExecuteAsync("GRANT CREATE ON SCHEMA public TO PUBLIC;").ConfigureAwait(false);
        }
    }

    private PostgresMvExecutor CreateVerifyAndExecuteExecutor(
        string connectionString,
        IMvSqlStatementPolicy policy,
        IMvExecutionObserver? executionObserver = null)
    {
        var registryStore = new PostgresMvRegistryStore(connectionString);
        var options = Options.Create(new MvOptions
        {
            ServiceId = MultiProviderFixtureBase.ServiceId,
            InitializationMode = MvInitializationMode.VerifyAndExecute,
            SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced,
            SqlStatementPolicy = policy,
            SafeWindowMs = 0,
            BatchSize = 100,
            ExecutionObserver = executionObserver
        });
        return new PostgresMvExecutor(
            fixture.EventStoreFactory,
            registryStore,
            options,
            NullLogger<PostgresMvExecutor>.Instance,
            connectionString);
    }

    private static SerializableEvent CreateEvent(PostgresMvFixture fixture, DateTime timestamp)
    {
        var eventId = Guid.CreateVersion7();
        var value = new Event(
            new WeatherForecastCreated(
                Guid.CreateVersion7(),
                "Tokyo",
                DateOnly.FromDateTime(timestamp),
                20,
                "Sunny"),
            SortableUniqueId.Generate(timestamp, eventId),
            nameof(WeatherForecastCreated),
            eventId,
            new EventMetadata("g34", "g34", "test"),
            []);
        return value.ToSerializableEvent(fixture.DomainTypes.EventTypes);
    }

    private sealed class ObservingPolicy(
        Func<long> readRows,
        Func<MvSqlStatementContext, MvSqlPolicyDecision> decision) : IMvSqlStatementPolicy
    {
        public List<MvSqlStatementContext> ApplyContexts { get; } = [];
        public List<long> ObservedRowsBeforeApply { get; } = [];

        public ValueTask<MvSqlPolicyDecision> EvaluateAsync(
            MvSqlStatementContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Phase == MvSqlStatementPhase.Apply)
            {
                ApplyContexts.Add(context);
                ObservedRowsBeforeApply.Add(readRows());
            }

            return ValueTask.FromResult(decision(context));
        }
    }

    private static bool IsDdlStatement(string sql) =>
        Regex.IsMatch(sql, @"^\s*(CREATE|ALTER|DROP)\b", RegexOptions.IgnoreCase);

    private sealed class ExecutionAudit : IMvExecutionObserver
    {
        public List<string> ProjectorCommandExecutionAttempts { get; } = [];
        public int TransactionCommitCount { get; private set; }

        public void OnProjectorCommandExecutionAttempt(string sql) =>
            ProjectorCommandExecutionAttempts.Add(sql);

        public void OnTransactionCommitted() => TransactionCommitCount++;

        public void Reset()
        {
            ProjectorCommandExecutionAttempts.Clear();
            TransactionCommitCount = 0;
        }
    }
}

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvVerifyOnlyTests(MySqlMvFixture fixture)
{
    [SkippableFact]
    public Task VerifyOnly_UsesTheCommonContractAgainstTheRealStore() =>
        MvVerifyOnlyAssertions.AssertCommonContractAsync(fixture);

    [SkippableFact]
    public Task SchemaContractMatrix_UsesTheProductionInspector() =>
        MvSchemaMatrixAssertions.AssertAsync(fixture);
}

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvVerifyOnlyTests(SqlServerMvFixture fixture)
{
    [SkippableFact]
    public Task VerifyOnly_UsesTheCommonContractAgainstTheRealStore() =>
        MvVerifyOnlyAssertions.AssertCommonContractAsync(fixture);

    [SkippableFact]
    public Task SchemaContractMatrix_UsesTheProductionInspector() =>
        MvSchemaMatrixAssertions.AssertAsync(fixture);

    [SkippableFact]
    public async Task VerifyOnlyUsesRestrictedPrincipalAndRejectsRealDml()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "SQL Server fixture is unavailable.");
        var inspectionConnectionString = fixture.InspectionConnectionStringForTests;
        Skip.If(string.IsNullOrWhiteSpace(inspectionConnectionString), "SQL Server inspection principal is unavailable.");

        await fixture.ResetAsync().ConfigureAwait(false);
        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        var host = new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests);
        await fixture.Executor.InitializeAsync(host).ConfigureAwait(false);

        await using var inspectionConnection = new SqlConnection(inspectionConnectionString);
        await inspectionConnection.OpenAsync().ConfigureAwait(false);
        var before = await inspectionConnection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM [{projector.Forecasts.PhysicalName}];")
            .ConfigureAwait(false);
        var dmlException = await Assert.ThrowsAsync<SqlException>(async () =>
            await inspectionConnection.ExecuteAsync(
                    $"INSERT INTO [{projector.Forecasts.PhysicalName}] (forecast_id, location, forecast_date, temperature_c, summary, is_deleted, _last_sortable_unique_id, _last_applied_at) VALUES ('readonly-probe', 'blocked', SYSUTCDATETIME(), 1, NULL, 0, 'readonly-probe', SYSUTCDATETIME());")
                .ConfigureAwait(false));
        Assert.Contains(dmlException.Errors.Cast<SqlError>(), error => error.Number == 229);
        var after = await inspectionConnection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM [{projector.Forecasts.PhysicalName}];")
            .ConfigureAwait(false);
        Assert.Equal(before, after);

        var options = new MvOptions();
        var bindings = new MvTableBindings(projector.ViewName, projector.ViewVersion, options);
        IReadOnlyList<MvSchemaTableRequirement> requirements =
        [
            .. MvSchemaRequirements.RegistryTables(),
            .. projector.GetSchemaRequirements(MvDbType.SqlServer, bindings)
        ];
        var inspector = new SqlServerMvRegistryStore(
            fixture.ConnectionStringForTests,
            null,
            null,
            inspectionConnectionString);
        var verification = await inspector.VerifySchemaAsync(requirements).ConfigureAwait(false);
        Assert.True(verification.IsCompatible, verification.Failure?.Message);
        var entries = await inspector.ReadRegistryEntriesAsync(
                MultiProviderFixtureBase.ServiceId,
                projector.ViewName,
                projector.ViewVersion)
            .ConfigureAwait(false);
        Assert.NotEmpty(entries);
    }
}

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvSchemaMatrixTests(SqliteMvFixture fixture)
{
    [SkippableFact]
    public Task SchemaContractMatrix_UsesTheProductionInspector() =>
        MvSchemaMatrixAssertions.AssertAsync(fixture);
}

internal static class MvSchemaMatrixAssertions
{
    public static async Task AssertAsync(MultiProviderFixtureBase fixture)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Provider fixture is unavailable.");
        await fixture.ResetAsync().ConfigureAwait(false);

        var tableName = $"sekiban_mv_schema_matrix_{Guid.NewGuid():N}";
        try
        {
            await CreateSchemaAsync(fixture, tableName).ConfigureAwait(false);
            var inspector = fixture.Services.GetRequiredService<IMvRegistryStore>() as IMvReadOnlyMvInspector;
            Assert.NotNull(inspector);
            var defaultSql = await ReadDefaultSqlAsync(fixture, tableName).ConfigureAwait(false);
            var generationExpression = await ReadGenerationExpressionAsync(fixture, tableName).ConfigureAwait(false);
            var requirement = CreateRequirement(
                fixture.DatabaseTypeForTests,
                tableName,
                defaultSql,
                generationExpression);

            var compatible = await inspector.VerifySchemaAsync([requirement]).ConfigureAwait(false);
            Assert.True(compatible.IsCompatible, compatible.Failure?.Message);

            if (fixture.DatabaseTypeForTests == MvDbType.Sqlite)
            {
                var unsupported = await inspector.VerifySchemaAsync(
                        [
                            new MvSchemaTableRequirement(
                                "schema_matrix",
                                tableName,
                                [
                                    new("id", MvSchemaTypeFamily.Integer, false),
                                    new("unbounded_value", MvSchemaTypeFamily.String, false) { MaxLength = 3 }
                                ],
                                ["id"])
                        ])
                    .ConfigureAwait(false);
                Assert.False(unsupported.IsCompatible);
                Assert.Equal(
                    MvInitializationFailureReason.UnsupportedProviderCapability,
                    unsupported.Failure?.Reason);
                Assert.Contains("declared character or binary length", unsupported.Failure?.Message, StringComparison.Ordinal);
            }

            await AssertSingleMismatchAsync(
                    inspector,
                    requirement with
                    {
                        Columns = requirement.Columns
                            .Select(column => column.Name == "base_value" ? column with { DefaultSql = "8" } : column)
                            .ToList()
                    },
                    MvSchemaMismatchCode.DefaultMismatch)
                .ConfigureAwait(false);
            await AssertSingleMismatchAsync(
                    inspector,
                    requirement with
                    {
                        Indexes = [new MvSchemaIndexRequirement("ux_schema_matrix", ["amount_value", "width_value"], true)]
                    },
                    MvSchemaMismatchCode.RequiredIndexMissing)
                .ConfigureAwait(false);
            await AssertSingleMismatchAsync(
                    inspector,
                    requirement with
                    {
                        Indexes = [new MvSchemaIndexRequirement("ix_schema_matrix_nonunique", ["base_value"], true)]
                    },
                    MvSchemaMismatchCode.RequiredIndexMissing)
                .ConfigureAwait(false);
            await AssertSingleMismatchAsync(
                    inspector,
                    requirement with
                    {
                        Columns = requirement.Columns
                            .Select(column => column.Name == "generated_value" ? column with { IsGenerated = false } : column)
                            .ToList()
                    },
                    MvSchemaMismatchCode.GeneratedSemanticsMismatch)
                .ConfigureAwait(false);
            await AssertSingleMismatchAsync(
                    inspector,
                    requirement with
                    {
                        Columns = requirement.Columns
                            .Select(column => column.Name == "generated_value"
                                ? column with { GenerationExpression = "base_value + 2" }
                                : column)
                            .ToList()
                    },
                    MvSchemaMismatchCode.GeneratedSemanticsMismatch)
                .ConfigureAwait(false);
            await AssertSingleMismatchAsync(
                    inspector,
                    requirement with
                    {
                        Columns = requirement.Columns
                            .Select(column => column.Name == "width_value" ? column with { MaxLength = 31 } : column)
                            .ToList()
                    },
                    MvSchemaMismatchCode.SizeMismatch)
                .ConfigureAwait(false);
            await AssertSingleMismatchAsync(
                    inspector,
                    requirement with
                    {
                        Columns = requirement.Columns
                            .Select(column => column.Name == "amount_value" ? column with { Precision = 9 } : column)
                            .ToList()
                    },
                    MvSchemaMismatchCode.PrecisionMismatch)
                .ConfigureAwait(false);
            await AssertSingleMismatchAsync(
                    inspector,
                    requirement with
                    {
                        Columns = requirement.Columns
                            .Select(column => column.Name == "amount_value" ? column with { Scale = 1 } : column)
                            .ToList()
                    },
                    MvSchemaMismatchCode.PrecisionMismatch)
                .ConfigureAwait(false);
        }
        finally
        {
            await DropSchemaAsync(fixture, tableName).ConfigureAwait(false);
        }
    }

    private static async Task AssertSingleMismatchAsync(
        IMvReadOnlyMvInspector inspector,
        MvSchemaTableRequirement requirement,
        MvSchemaMismatchCode expectedCode)
    {
        var result = await inspector.VerifySchemaAsync([requirement]).ConfigureAwait(false);
        Assert.False(result.IsCompatible);
        Assert.Equal([expectedCode], result.Mismatches.Select(mismatch => mismatch.Code));
        Assert.Equal([expectedCode], result.Failure?.Mismatches.Select(mismatch => mismatch.Code));
    }

    private static MvSchemaTableRequirement CreateRequirement(
        MvDbType databaseType,
        string tableName,
        string defaultSql,
        string generationExpression) =>
        new(
            "schema_matrix",
            tableName,
            [
                new("id", MvSchemaTypeFamily.Integer, false),
                new("base_value", MvSchemaTypeFamily.Integer, false) { DefaultSql = defaultSql },
                new("width_value", MvSchemaTypeFamily.String, false) { MaxLength = 32 },
                new("amount_value", MvSchemaTypeFamily.Decimal, false) { Precision = 10, Scale = 2 },
                new("generated_value", MvSchemaTypeFamily.Integer, databaseType != MvDbType.SqlServer)
                {
                    IsGenerated = true,
                    GenerationExpression = generationExpression
                }
            ],
            ["id"])
        {
            Indexes =
            [
                new MvSchemaIndexRequirement("ux_schema_matrix", ["width_value", "amount_value"], true),
                new MvSchemaIndexRequirement("ix_schema_matrix_nonunique", ["base_value"], false)
            ]
        };

    private static async Task CreateSchemaAsync(MultiProviderFixtureBase fixture, string tableName)
    {
        var table = QuoteIdentifier(fixture.DatabaseTypeForTests, tableName);
        var index = QuoteIdentifier(fixture.DatabaseTypeForTests, $"ux_{tableName}");
        var sql = fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => $"""
                CREATE TABLE {table} (
                    id INTEGER NOT NULL PRIMARY KEY,
                    base_value INTEGER NOT NULL DEFAULT 7,
                    width_value VARCHAR(32) NOT NULL,
                    amount_value DECIMAL(10, 2) NOT NULL,
                    generated_value INTEGER GENERATED ALWAYS AS (base_value + 1) STORED
                );
                CREATE UNIQUE INDEX {index} ON {table} (width_value, amount_value);
                CREATE INDEX ix_{tableName} ON {table} (base_value);
                """,
            MvDbType.MySql => $"""
                CREATE TABLE {table} (
                    id INT NOT NULL PRIMARY KEY,
                    base_value INT NOT NULL DEFAULT 7,
                    width_value VARCHAR(32) NOT NULL,
                    amount_value DECIMAL(10, 2) NOT NULL,
                    generated_value INT GENERATED ALWAYS AS (base_value + 1) STORED
                );
                CREATE UNIQUE INDEX {index} ON {table} (width_value, amount_value);
                CREATE INDEX ix_{tableName} ON {table} (base_value);
                """,
            MvDbType.SqlServer => $"""
                CREATE TABLE {table} (
                    id INT NOT NULL PRIMARY KEY,
                    base_value INT NOT NULL CONSTRAINT {QuoteIdentifier(fixture.DatabaseTypeForTests, $"df_{tableName}")} DEFAULT 7,
                    width_value VARCHAR(32) NOT NULL,
                    amount_value DECIMAL(10, 2) NOT NULL,
                    generated_value AS (ISNULL(base_value + 1, 0)) PERSISTED
                );
                CREATE UNIQUE INDEX {index} ON {table} (width_value, amount_value);
                CREATE INDEX ix_{tableName} ON {table} (base_value);
                """,
            MvDbType.Sqlite => $"""
                CREATE TABLE {table} (
                    id INTEGER NOT NULL PRIMARY KEY,
                    base_value INTEGER NOT NULL DEFAULT 7,
                    width_value VARCHAR(32) NOT NULL,
                    amount_value DECIMAL(10, 2) NOT NULL,
                    unbounded_value TEXT NOT NULL,
                    generated_value INTEGER GENERATED ALWAYS AS (base_value + 1) STORED
                );
                CREATE UNIQUE INDEX {index} ON {table} (width_value, amount_value);
                CREATE INDEX ix_{tableName} ON {table} (base_value);
                """,
            _ => throw new NotSupportedException()
        };
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private static async Task<string> ReadDefaultSqlAsync(MultiProviderFixtureBase fixture, string tableName)
    {
        var sql = fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => "SELECT column_default FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @TableName AND column_name = 'base_value';",
            MvDbType.MySql => "SELECT column_default FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @TableName AND column_name = 'base_value';",
            MvDbType.SqlServer => "SELECT defaults.definition FROM sys.default_constraints AS defaults INNER JOIN sys.columns AS columns ON columns.default_object_id = defaults.object_id INNER JOIN sys.tables AS tables ON tables.object_id = columns.object_id WHERE tables.name = @TableName AND columns.name = 'base_value';",
            MvDbType.Sqlite => "SELECT dflt_value FROM pragma_table_info(@TableName) WHERE name = 'base_value';",
            _ => throw new NotSupportedException()
        };
        await using var connection = await OpenCatalogConnectionAsync(fixture).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<string?>(sql, new { TableName = tableName }).ConfigureAwait(false) ??
            throw new InvalidOperationException($"Schema matrix default metadata was not found for '{tableName}'.");
    }

    private static async Task<string> ReadGenerationExpressionAsync(
        MultiProviderFixtureBase fixture,
        string tableName)
    {
        var sql = fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => "SELECT generation_expression FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @TableName AND column_name = 'generated_value';",
            MvDbType.MySql => "SELECT generation_expression FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @TableName AND column_name = 'generated_value';",
            MvDbType.SqlServer => "SELECT computed_columns.definition FROM sys.computed_columns AS computed_columns INNER JOIN sys.tables AS tables ON tables.object_id = computed_columns.object_id WHERE tables.name = @TableName AND computed_columns.name = 'generated_value';",
            MvDbType.Sqlite => "SELECT 'base_value + 1';",
            _ => throw new NotSupportedException()
        };
        await using var connection = await OpenCatalogConnectionAsync(fixture).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<string?>(sql, new { TableName = tableName }).ConfigureAwait(false) ??
            throw new InvalidOperationException($"Schema matrix generated metadata was not found for '{tableName}'.");
    }

    private static async Task<DbConnection> OpenCatalogConnectionAsync(MultiProviderFixtureBase fixture)
    {
        if (fixture.DatabaseTypeForTests == MvDbType.SqlServer &&
            !string.IsNullOrWhiteSpace(fixture.InspectionConnectionStringForTests))
        {
            var inspectionConnection = new SqlConnection(fixture.InspectionConnectionStringForTests);
            await inspectionConnection.OpenAsync().ConfigureAwait(false);
            return inspectionConnection;
        }

        return await fixture.OpenConnectionAsync().ConfigureAwait(false);
    }

    private static async Task DropSchemaAsync(MultiProviderFixtureBase fixture, string tableName)
    {
        if (!fixture.IsAvailable)
        {
            return;
        }

        var table = QuoteIdentifier(fixture.DatabaseTypeForTests, tableName);
        var index = QuoteIdentifier(fixture.DatabaseTypeForTests, $"ux_{tableName}");
        var sql = fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => $"DROP INDEX IF EXISTS {index}; DROP TABLE IF EXISTS {table};",
            MvDbType.MySql => $"DROP TABLE IF EXISTS {table};",
            MvDbType.SqlServer => $"IF OBJECT_ID(N'{tableName}', N'U') IS NOT NULL DROP TABLE {table};",
            MvDbType.Sqlite => $"DROP TABLE IF EXISTS {table};",
            _ => throw new NotSupportedException()
        };
        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private static string QuoteIdentifier(MvDbType databaseType, string identifier) => databaseType switch
    {
        MvDbType.MySql => $"`{identifier}`",
        MvDbType.SqlServer => $"[{identifier}]",
        _ => $"\"{identifier}\""
    };
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
        var readOnlyConnections = new List<string>();
        var verifyExecutor = CreateVerifyExecutor(fixture, policy, catalogCommands.Add, readOnlyConnections.Add);
        var verifyHost = new CountingApplyHost(
            new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.DatabaseTypeForTests));
        await verifyExecutor.InitializeAsync(
                verifyHost)
            .ConfigureAwait(false);

        Assert.Equal(0, verifyHost.InitializeCalls);
        Assert.Empty(policy.Contexts);
        Assert.NotEmpty(catalogCommands);
        Assert.Contains(ExpectedReadOnlyMarker(fixture.DatabaseTypeForTests), readOnlyConnections);
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
        Action<string>? catalogCommandRecorder,
        Action<string>? readOnlyConnectionRecorder)
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
                new PostgresMvRegistryStore(connectionString, catalogCommandRecorder, readOnlyConnectionRecorder),
                options,
                NullLogger<PostgresMvExecutor>.Instance,
                connectionString),
            MvDbType.MySql => new MySqlMvExecutor(
                fixture.EventStoreFactory,
                new MySqlMvRegistryStore(connectionString, catalogCommandRecorder, readOnlyConnectionRecorder),
                options,
                NullLogger<MySqlMvExecutor>.Instance,
                connectionString),
            MvDbType.SqlServer => new SqlServerMvExecutor(
                fixture.EventStoreFactory,
                new SqlServerMvRegistryStore(
                    connectionString,
                    catalogCommandRecorder,
                    readOnlyConnectionRecorder,
                    fixture.InspectionConnectionStringForTests),
                options,
                NullLogger<SqlServerMvExecutor>.Instance,
                connectionString),
            MvDbType.Sqlite => new SqliteMvExecutor(
                fixture.EventStoreFactory,
                new SqliteMvRegistryStore(connectionString, catalogCommandRecorder, readOnlyConnectionRecorder),
                options,
                NullLogger<SqliteMvExecutor>.Instance,
                connectionString),
            _ => throw new NotSupportedException($"Provider '{fixture.DatabaseTypeForTests}' is not supported.")
        };
    }

    private static string ExpectedReadOnlyMarker(MvDbType databaseType) => databaseType switch
    {
        MvDbType.Postgres => "postgres:default_transaction_read_only=on",
        MvDbType.MySql => "mysql:transaction_read_only=on",
        MvDbType.SqlServer => "sqlserver:restricted-inspection-principal",
        MvDbType.Sqlite => "sqlite:Mode=ReadOnly",
        _ => throw new ArgumentOutOfRangeException(nameof(databaseType))
    };

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
