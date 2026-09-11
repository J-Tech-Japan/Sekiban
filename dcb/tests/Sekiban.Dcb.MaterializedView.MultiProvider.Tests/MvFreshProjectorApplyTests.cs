using Dapper;
using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;
using Sekiban.Dcb.MaterializedView.Postgres;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

public sealed record FreshProjectorBindingScenario(string Name)
{
    public override string ToString() => Name;
}

[Collection(nameof(PostgresMvCollection))]
public sealed class PostgresMvFreshProjectorApplyTests(PostgresMvFixture fixture)
{
    [SkippableTheory]
    [InlineData("default")]
    [InlineData("prefix")]
    [InlineData("resolver")]
    [InlineData("explicit")]
    public Task FreshProjector_VerifyAndExecuteUsesApplyTimeOwnTables(string scenario) =>
        FreshProjectorApplyAssertions.AssertAsync(fixture, new FreshProjectorBindingScenario(scenario));
}

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvFreshProjectorApplyTests(SqlServerMvFixture fixture)
{
    [SkippableTheory]
    [InlineData("default")]
    [InlineData("prefix")]
    [InlineData("resolver")]
    [InlineData("explicit")]
    public Task FreshProjector_VerifyAndExecuteUsesApplyTimeOwnTables(string scenario) =>
        FreshProjectorApplyAssertions.AssertAsync(fixture, new FreshProjectorBindingScenario(scenario));
}

internal static class FreshProjectorApplyAssertions
{
    private const string ViewName = "WeatherForecastPortable";
    private const int Version = 1;

    public static async Task AssertAsync(
        MultiProviderFixtureBase fixture,
        FreshProjectorBindingScenario scenario)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.AvailabilityMessage ?? "Integration fixture is unavailable.");

        var preparationOptions = CreateOptions(scenario);
        preparationOptions.InitializationMode = MvInitializationMode.CreateOrEnsure;
        var tableName = MvPhysicalName.Resolve(preparationOptions, ViewName, Version, "forecasts");
        await fixture.ResetAsync().ConfigureAwait(false);
        await DropTableAsync(fixture, tableName).ConfigureAwait(false);

        var storageInfo = fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo();
        using (var preparationProvider = CreateProjectorProvider(storageInfo))
        {
            var preparationProjector = preparationProvider.GetRequiredService<CrossProviderWeatherForecastMvV1>();
            var preparationFactory = preparationProvider.GetRequiredService<IMvApplyHostFactory>();
            var preparationHost = preparationFactory.Create(
                ViewName,
                Version);
            var preparationExecutor = CreateExecutor(fixture, preparationOptions);

            await preparationExecutor.InitializeAsync(preparationHost).ConfigureAwait(false);
            Assert.Equal(1, preparationProjector.InitializeCallCount);
        }

        var serializableEvent = CreateEvent(fixture);
        var writeResult = await fixture.EventStore.WriteSerializableEventsAsync([serializableEvent]).ConfigureAwait(false);
        Assert.True(writeResult.IsSuccess, writeResult.IsSuccess ? string.Empty : writeResult.GetException().Message);

        using (var executionProvider = CreateProjectorProvider(storageInfo))
        {
            var executionProjector = executionProvider.GetRequiredService<CrossProviderWeatherForecastMvV1>();
            var executionFactory = executionProvider.GetRequiredService<IMvApplyHostFactory>();
            var executionHost = executionFactory.Create(
                ViewName,
                Version);
            var executionOptions = CreateOptions(scenario);
            executionOptions.InitializationMode = MvInitializationMode.VerifyAndExecute;
            executionOptions.SqlStatementPolicyMode = MvSqlStatementPolicyMode.Enforced;
            executionOptions.SqlStatementPolicy = new OwnTablesOnlyPolicy();
            var executionAudit = new VerifiedBatchExecutionAudit();
            executionOptions.ExecutionObserver = executionAudit;
            if (fixture.DatabaseTypeForTests == MvDbType.SqlServer)
            {
                executionOptions.SqlServerInspectionConnectionString =
                    fixture.InspectionConnectionStringForTests;
            }
            var executionExecutor = CreateExecutor(fixture, executionOptions);

            var applied = await executionExecutor.ApplySerializableEventsAsync(
                    executionHost,
                    [serializableEvent],
                    MultiProviderFixtureBase.ServiceId)
                .ConfigureAwait(false);

            Assert.Equal(1, applied);
            Assert.Equal(0, executionProjector.InitializeCallCount);
            Assert.Equal(1, executionProjector.ApplyCallCount);
            Assert.Equal(1, executionAudit.TransactionCommitCount);
            Assert.NotEmpty(executionAudit.ProjectorCommandExecutionAttempts);
            Assert.All(
                executionAudit.ProjectorCommandExecutionAttempts,
                sql => Assert.Contains(tableName, sql, StringComparison.OrdinalIgnoreCase));

            var registryStore = fixture.Services.GetRequiredService<IMvRegistryStore>();
            var entry = Assert.Single(await registryStore.GetEntriesAsync(
                    MultiProviderFixtureBase.ServiceId,
                    ViewName,
                    Version)
                .ConfigureAwait(false));
            Assert.Equal(tableName, entry.PhysicalTable);
            Assert.Equal(serializableEvent.SortableUniqueIdValue, entry.CurrentCheckpointTruth.PositionValue);

            await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
            var rowCount = await connection.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM {entry.PhysicalTable} WHERE forecast_id = @ForecastId;",
                    new { ForecastId = ForecastId.ToString("D") })
                .ConfigureAwait(false);
            Assert.Equal(1, rowCount);
        }
    }

    private static ServiceProvider CreateProjectorProvider(MvStorageInfo storageInfo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(DomainType.GetDomainTypes());
        services.AddSingleton<IMvStorageInfoProvider>(new FixedStorageInfoProvider(storageInfo));
        services.AddSekibanDcbMaterializedView();
        services.AddMaterializedView<CrossProviderWeatherForecastMvV1>();
        return services.BuildServiceProvider();
    }

    private static IMvExecutor CreateExecutor(MultiProviderFixtureBase fixture, MvOptions options) =>
        fixture.DatabaseTypeForTests switch
        {
            MvDbType.Postgres => new PostgresMvExecutor(
                fixture.EventStoreFactory,
                new PostgresMvRegistryStore(fixture.ConnectionStringForTests),
                Options.Create(options),
                NullLogger<PostgresMvExecutor>.Instance,
                fixture.ConnectionStringForTests),
            MvDbType.SqlServer => new SqlServerMvExecutor(
                fixture.EventStoreFactory,
                new SqlServerMvRegistryStore(
                    fixture.ConnectionStringForTests,
                    null,
                    null,
                    options.SqlServerInspectionConnectionString),
                Options.Create(options),
                NullLogger<SqlServerMvExecutor>.Instance,
                fixture.ConnectionStringForTests),
            _ => throw new NotSupportedException($"Unsupported provider '{fixture.DatabaseTypeForTests}'.")
        };

    private static MvOptions CreateOptions(FreshProjectorBindingScenario scenario) =>
        new()
        {
            ServiceId = MultiProviderFixtureBase.ServiceId,
            SafeWindowMs = 0,
            TablePrefix = scenario.Name == "prefix" ? "g73_custom_prefix" : MvOptions.DefaultTablePrefix,
            PhysicalNameResolver = scenario.Name switch
            {
                "resolver" => (_, _, logical) => $"g73_resolved_{logical}",
                "explicit" => (_, _, _) => "g73_explicit_forecasts",
                _ => null
            }
        };

    private static readonly Guid ForecastId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static SerializableEvent CreateEvent(MultiProviderFixtureBase fixture)
    {
        var eventId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var value = new Event(
            new WeatherForecastCreated(
                ForecastId,
                "G73",
                new DateOnly(2026, 9, 10),
                21,
                "Own table binding"),
            SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-1), eventId),
            nameof(WeatherForecastCreated),
            eventId,
            new EventMetadata("g73", "g73", "fresh-projector"),
            []);
        return value.ToSerializableEvent(fixture.DomainTypes.EventTypes);
    }

    private static async Task DropTableAsync(MultiProviderFixtureBase fixture, string tableName)
    {
        if (tableName == MultiProviderFixtureBase.ForecastTable)
        {
            return;
        }

        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        var sql = fixture.DatabaseTypeForTests == MvDbType.SqlServer
            ? $"IF OBJECT_ID(N'{tableName}', N'U') IS NOT NULL DROP TABLE {tableName};"
            : $"DROP TABLE IF EXISTS {tableName};";
        await connection.ExecuteAsync(sql).ConfigureAwait(false);
    }

    private sealed class FixedStorageInfoProvider(MvStorageInfo storageInfo) : IMvStorageInfoProvider
    {
        public MvStorageInfo GetStorageInfo() => storageInfo;
    }

}
