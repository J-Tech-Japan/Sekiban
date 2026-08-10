using Dcb.Domain.WithoutResult;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.Tests;

public sealed class MvActivationEligibilityTests
{
    [Fact]
    public void MissingCandidate_HasTypedRejection() =>
        AssertRejected(
            MvActivationFailureReason.CandidateMissing,
            "orders",
            "Weather",
            2,
            []);

    [Fact]
    public void CandidateIdentityMismatch_HasTypedRejection()
    {
        var entry = EligibleEntry() with { ServiceId = "billing" };
        AssertRejected(MvActivationFailureReason.IdentityMismatch, "orders", "Weather", 2, [entry]);
    }

    [Fact]
    public void FaultedLifecycle_HasTypedRejection() =>
        AssertRejected(MvActivationFailureReason.Faulted, entries: [EligibleEntry() with { Status = MvStatus.Faulted }]);

    [Fact]
    public void NonReadyLifecycle_HasTypedRejection() =>
        AssertRejected(MvActivationFailureReason.UnsafeLifecycle, entries: [EligibleEntry() with { Status = MvStatus.CatchingUp }]);

    [Fact]
    public void UnknownTarget_IsRejectedBeforeAnyActivationRequestIsCreated()
    {
        var current = KnownCheckpoint("current", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var entry = CreateEntry(
            "orders",
            "Weather",
            2,
            MvStatus.Ready,
            current,
            MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable));

        var (eligibility, request) = MvActivationEligibility.Evaluate(
            "orders",
            "Weather",
            2,
            [entry],
            active: null);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(MvActivationFailureReason.TargetUnknown, eligibility.FailureReason);
        Assert.Null(request);
    }

    [Fact]
    public void UnknownCurrent_HasTypedRejection() =>
        AssertRejected(
            MvActivationFailureReason.CurrentCheckpointUnknown,
            entries:
            [
                EligibleEntry() with
                {
                    CurrentPosition = null,
                    CurrentCheckpointTruth = MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable)
                }
            ]);

    [Fact]
    public void MissingAuthoritativeProvenance_HasTypedRejection()
    {
        var same = KnownCheckpoint("same", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        AssertRejected(
            MvActivationFailureReason.MissingProvenance,
            entries: [CreateEntry("orders", "Weather", 2, MvStatus.Ready, same, same)]);
    }

    [Fact]
    public void LegacyPositionMismatch_HasTypedRejection() =>
        AssertRejected(
            MvActivationFailureReason.CandidateStateMismatch,
            entries: [EligibleEntry() with { CurrentPosition = KnownCheckpoint("target", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp)).PositionValue }]);

    [Fact]
    public async Task ProductionSqliteActivation_FavorableG24ObservationCannotAuthorizeUnknownTruth()
    {
        const string serviceId = "orders";
        var connectionString = $"Data Source=file:sek-g27-g24-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();

        var serviceIdProvider = new FixedServiceIdProvider(serviceId);
        var statusStore = new InMemoryMultiProjectionStateStore(serviceIdProvider);
        var eventStore = new InMemoryEventStore(DomainType.GetDomainTypes().EventTypes, serviceIdProvider);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(DomainType.GetDomainTypes());
        services.AddSingleton<IServiceIdProvider>(serviceIdProvider);
        services.AddSingleton(eventStore);
        services.AddSingleton<IEventStore>(eventStore);
        services.AddSingleton<IEventStoreFactory>(new Sekiban.Dcb.InMemory.InMemoryEventStoreFactory(eventStore));
        services.AddSingleton(statusStore);
        services.AddSingleton<IProjectionStatusStore>(statusStore);
        services.AddSingleton(new ProjectionStatusOptions { FreshnessWindow = TimeSpan.FromMinutes(1) });
        services.AddSekibanDcbProjectionStatusReader();
        services.AddSekibanDcbMaterializedView(options => options.ServiceId = serviceId);
        services.AddSekibanDcbMaterializedViewSqlite(connectionString, registerHostedWorker: false);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IMvRegistryStore>();
        await registry.EnsureInfrastructureAsync();
        var current = KnownCheckpoint("current", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        await registry.RegisterAsync(
            CreateEntry(
                serviceId,
                "Weather",
                2,
                MvStatus.Ready,
                current,
                MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.ReadUnavailable)));

        var now = DateTimeOffset.UtcNow;
        var write = await statusStore.UpsertAsync(
            new ProjectionStatusHeartbeat(
                serviceId,
                "Weather",
                "2",
                "cluster-a",
                "activation-a",
                1,
                0,
                null,
                null,
                now)
            {
                Phase = ProjectionStatusPhases.CaughtUp,
                LeaseExpiresAtUtc = now.AddMinutes(1)
            },
            expectedSequence: 0);
        Assert.True(write.IsSuccess);
        var statusResult = await provider.GetRequiredService<IProjectionStatusReader>()
            .ReadAsync(new ProjectionStatusReadRequest(serviceId, "Weather", "2"));
        Assert.True(statusResult.IsSuccess);
        var g24Observation = Assert.Single(statusResult.GetValue());
        Assert.True(g24Observation.IsCaughtUp);

        var providerExecutor = Assert.IsType<SqliteMvExecutor>(provider.GetRequiredService<IMvExecutor>());
        var activationExecutor = Assert.IsAssignableFrom<IMvActivationExecutor>(providerExecutor);
        var activePointerWritesBefore = await CountActivePointersAsync(anchor);
        var result = await activationExecutor.TryActivateAsync(new ActivationHost(), serviceId);

        Assert.False(result.Succeeded);
        Assert.Equal(MvActivationFailureReason.TargetUnknown, result.FailureReason);
        Assert.Null(await registry.GetActiveAsync(serviceId, "Weather"));
        Assert.Equal(activePointerWritesBefore, await CountActivePointersAsync(anchor));
        Assert.Equal(0, activePointerWritesBefore);
    }

    [Fact]
    public void ProductionActivationSurface_HasNoG24AuthorizationDependency()
    {
        Type[] productionActivationTypes =
        [
            typeof(MvExecutorBase<>),
            typeof(MySqlMvExecutor),
            typeof(PostgresMvExecutor),
            typeof(SqlServerMvExecutor),
            typeof(SqliteMvExecutor)
        ];

        var dependencyTypes = productionActivationTypes.SelectMany(type =>
            type.GetConstructors().SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType))
                .Concat(type.GetFields(System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.DeclaredOnly).Select(field => field.FieldType)));

        Assert.DoesNotContain(dependencyTypes, IsG24AuthorizationType);
    }

    [Fact]
    public void CandidateRowsWithDifferentTruthSnapshots_HaveTypedRejection()
    {
        var first = EligibleEntry() with { LogicalTable = "first" };
        var later = MvCheckpointTruth.Known(
            new SortableUniqueId(SortableUniqueId.Generate(
                DateTime.UnixEpoch.AddMinutes(2),
                GuidUtility.Create("target"))),
            MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var second = EligibleEntry() with
        {
            LogicalTable = "second",
            CurrentPosition = later.PositionValue,
            CurrentCheckpointTruth = later
        };
        AssertRejected(MvActivationFailureReason.CandidateStateMismatch, entries: [first, second]);
    }

    [Fact]
    public void ActivePointerIdentityMismatch_HasTypedRejection() =>
        AssertRejected(
            MvActivationFailureReason.IdentityMismatch,
            entries: [EligibleEntry()],
            active: new MvActiveEntry("billing", "Weather", 1, DateTimeOffset.UtcNow));

    [Fact]
    public void AlreadyActive_HasTypedRejection() =>
        AssertRejected(
            MvActivationFailureReason.AlreadyActive,
            entries: [EligibleEntry()],
            active: new MvActiveEntry("orders", "Weather", 2, DateTimeOffset.UtcNow));

    [Fact]
    public void EligibleCandidate_CarriesExpectedActiveAndGenerationSnapshot()
    {
        var checkpoint = KnownCheckpoint("same", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var target = KnownCheckpoint("same", MvCheckpointProvenance.AuthoritativeTargetCapture());
        var entry = CreateEntry("orders", "Weather", 2, MvStatus.Ready, checkpoint, target);
        var active = new MvActiveEntry("orders", "Weather", 1, DateTimeOffset.UtcNow)
        {
            Generation = 7
        };

        var (eligibility, request) = MvActivationEligibility.Evaluate(
            "orders",
            "Weather",
            2,
            [entry],
            active);

        Assert.True(eligibility.IsEligible);
        Assert.NotNull(request);
        Assert.Equal(1, request.ExpectedActiveVersion);
        Assert.Equal(7, request.ExpectedActiveGeneration);
        Assert.Equal(MvStatus.Ready, request.ExpectedStatus);
    }

    [Fact]
    public async Task SqliteRegistry_UsesExpectedGenerationAndLeavesPointerUnchangedForInvalidOrStaleRequests()
    {
        var connectionString = $"Data Source=file:sek-g27-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();

        var store = new SqliteMvRegistryStore(connectionString);
        await store.EnsureInfrastructureAsync();

        var checkpoint = KnownCheckpoint("same", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var target = KnownCheckpoint("same", MvCheckpointProvenance.AuthoritativeTargetCapture());
        await store.RegisterAsync(CreateEntry("orders", "Weather", 2, MvStatus.Ready, checkpoint, target));

        var entries = await store.GetEntriesAsync("orders", "Weather", 2);
        var (eligibility, request) = MvActivationEligibility.Evaluate("orders", "Weather", 2, entries, null);
        Assert.True(eligibility.IsEligible);
        Assert.NotNull(request);

        var invalid = await store.TryActivateAsync(
            request! with
            {
                ExpectedTargetCheckpointTruth = MvCheckpointTruthCodec.Encode(
                    MvCheckpointTruth.Unknown(MvCheckpointUnknownReason.Malformed))
            });
        Assert.False(invalid.Succeeded);
        Assert.Equal(MvActivationFailureReason.TargetUnknown, invalid.FailureReason);
        Assert.Null(await store.GetActiveAsync("orders", "Weather"));

        var winner = await store.TryActivateAsync(request!);
        var stale = await store.TryActivateAsync(request!);

        Assert.True(winner.Succeeded);
        Assert.Equal(1, winner.NewGeneration);
        Assert.False(stale.Succeeded);
        Assert.True(stale.IsConflict);

        var active = await store.GetActiveAsync("orders", "Weather");
        Assert.NotNull(active);
        Assert.Equal(2, active.ActiveVersion);
        Assert.Equal(1, active.Generation);
        Assert.All(
            await store.GetEntriesAsync("orders", "Weather", 2),
            entry => Assert.Equal(MvStatus.Active, entry.Status));
    }

    private static MvRegistryEntry CreateEntry(
        string serviceId,
        string viewName,
        int viewVersion,
        MvStatus status,
        MvCheckpointTruth current,
        MvCheckpointTruth target) =>
        new()
        {
            ServiceId = serviceId,
            ViewName = viewName,
            ViewVersion = viewVersion,
            LogicalTable = "main",
            PhysicalTable = "weather_main",
            Status = status,
            CurrentPosition = current.PositionValue,
            TargetPosition = target.PositionValue,
            CurrentCheckpointTruth = current,
            TargetCheckpointTruth = target,
            AppliedEventVersion = 1,
            LastUpdated = DateTimeOffset.UtcNow
        };

    private static MvRegistryEntry EligibleEntry()
    {
        var current = KnownCheckpoint("same", MvCheckpointProvenance.AppliedEvent(MvApplySource.CatchUp));
        var target = KnownCheckpoint("same", MvCheckpointProvenance.AuthoritativeTargetCapture());
        return CreateEntry("orders", "Weather", 2, MvStatus.Ready, current, target);
    }

    private static void AssertRejected(
        MvActivationFailureReason expected,
        string serviceId = "orders",
        string viewName = "Weather",
        int viewVersion = 2,
        IReadOnlyList<MvRegistryEntry>? entries = null,
        MvActiveEntry? active = null)
    {
        var (eligibility, request) = MvActivationEligibility.Evaluate(
            serviceId,
            viewName,
            viewVersion,
            entries ?? [EligibleEntry()],
            active);
        Assert.False(eligibility.IsEligible);
        Assert.Equal(expected, eligibility.FailureReason);
        Assert.Null(request);
    }

    private static MvCheckpointTruth KnownCheckpoint(
        string seed,
        MvCheckpointProvenance provenance)
    {
        var id = GuidUtility.Create(seed);
        return MvCheckpointTruth.Known(
            new SortableUniqueId(SortableUniqueId.Generate(DateTime.UnixEpoch.AddMinutes(1), id)),
            provenance);
    }

    private static async Task<long> CountActivePointersAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sekiban_mv_active";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static bool IsG24AuthorizationType(Type type) =>
        type == typeof(IProjectionStatusReader) ||
        type == typeof(ProjectionStatusSnapshot) ||
        type == typeof(ProjectionStatusReadRequest);

    private sealed class ActivationHost : IMvApplyHost
    {
        public string ViewName => "Weather";
        public int ViewVersion => 2;
        public IReadOnlyList<string> LogicalTables => ["main"];

        public Task<IReadOnlyList<MvSqlStatementDto>> InitializeAsync(
            IMvTableBindings tables,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);

        public Task<IReadOnlyList<MvSqlStatementDto>> ApplyEventAsync(
            SerializableEvent ev,
            IMvTableBindings tables,
            IMvApplyQueryPort queryPort,
            string sortableUniqueId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MvSqlStatementDto>>([]);
    }
}

internal static class GuidUtility
{
    public static Guid Create(string value) =>
        new(value switch
        {
            "current" => "00000000-0000-0000-0000-000000000001",
            "target" => "00000000-0000-0000-0000-000000000002",
            "same" => "00000000-0000-0000-0000-000000000003",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        });
}
