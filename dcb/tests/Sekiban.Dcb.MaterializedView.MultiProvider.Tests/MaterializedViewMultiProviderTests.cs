using Dapper;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.MaterializedView.MySql;
using Sekiban.Dcb.MaterializedView.Sqlite;
using Sekiban.Dcb.MaterializedView.SqlServer;
using Xunit;

namespace Sekiban.Dcb.MaterializedView.MultiProvider.Tests;

[CollectionDefinition(nameof(MySqlMvCollection))]
public sealed class MySqlMvCollection : ICollectionFixture<MySqlMvFixture>;

[CollectionDefinition(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvCollection : ICollectionFixture<SqlServerMvFixture>;

[CollectionDefinition(nameof(SqliteMvCollection))]
public sealed class SqliteMvCollection : ICollectionFixture<SqliteMvFixture>;

public sealed class MaterializedViewMultiProviderRegistrationTests
{
    [Fact]
    public void SqlServer_Registration_ReportsStorageInfo()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbMaterializedViewSqlServer("Server=(local);Database=test;User Id=sa;Password=Password123!;", registerHostedWorker: false);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo();
        Assert.Equal(MvDbType.SqlServer, storage.DatabaseType);
    }

    [Fact]
    public void MySql_Registration_ReportsStorageInfo()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbMaterializedViewMySql("Server=localhost;Database=test;User Id=root;Password=test;", registerHostedWorker: false);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo();
        Assert.Equal(MvDbType.MySql, storage.DatabaseType);
    }

    [Fact]
    public void Sqlite_Registration_ReportsStorageInfo()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbMaterializedViewSqlite("Data Source=:memory:", registerHostedWorker: false);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo();
        Assert.Equal(MvDbType.Sqlite, storage.DatabaseType);
    }
}

[Collection(nameof(MySqlMvCollection))]
public sealed class MySqlMvIntegrationTests(MySqlMvFixture fixture)
{
    [Fact]
    public Task CatchUp_MaterializesRowsAndRegistry() => MultiProviderAssertions.AssertProviderWorksAsync(fixture);
}

[Collection(nameof(SqlServerMvCollection))]
public sealed class SqlServerMvIntegrationTests(SqlServerMvFixture fixture)
{
    [Fact]
    public Task CatchUp_MaterializesRowsAndRegistry() => MultiProviderAssertions.AssertProviderWorksAsync(fixture);
}

[Collection(nameof(SqliteMvCollection))]
public sealed class SqliteMvIntegrationTests(SqliteMvFixture fixture)
{
    [Fact]
    public Task CatchUp_MaterializesRowsAndRegistry() => MultiProviderAssertions.AssertProviderWorksAsync(fixture);
}

internal static class MultiProviderAssertions
{
    public static async Task AssertProviderWorksAsync(MultiProviderFixtureBase fixture)
    {
        if (!fixture.IsAvailable)
        {
            fixture.EnsureAvailable();
            return;
        }

        await fixture.ResetAsync().ConfigureAwait(false);

        var projector = fixture.Services.GetRequiredService<CrossProviderWeatherForecastMvV1>();
        await fixture.Executor.InitializeAsync(new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo().DatabaseType))
            .ConfigureAwait(false);

        var executor = new GeneralSekibanExecutor(fixture.EventStore, fixture.ActorAccessor, fixture.DomainTypes);
        var forecastId = Guid.CreateVersion7();
        var forecastDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        await executor.ExecuteAsync(new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Tokyo",
            Date = forecastDate,
            TemperatureC = 20,
            Summary = "Sunny"
        }).ConfigureAwait(false);

        await executor.ExecuteAsync(new UpdateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Kyoto",
            Date = forecastDate.AddDays(1),
            TemperatureC = 21,
            Summary = "Cloudy"
        }).ConfigureAwait(false);

        await executor.ExecuteAsync(new DeleteWeatherForecast
        {
            ForecastId = forecastId
        }).ConfigureAwait(false);

        var firstCatchUp = await fixture.Executor.CatchUpOnceAsync(
            new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo().DatabaseType))
            .ConfigureAwait(false);
        var secondCatchUp = await fixture.Executor.CatchUpOnceAsync(
            new NativeMvApplyHost(projector, fixture.DomainTypes.EventTypes, fixture.Services.GetRequiredService<IMvStorageInfoProvider>().GetStorageInfo().DatabaseType))
            .ConfigureAwait(false);

        await using var connection = await fixture.OpenConnectionAsync().ConfigureAwait(false);
        var row = await connection.QuerySingleAsync<ForecastDbRow>(
            $"""
             SELECT forecast_id AS ForecastId,
                    location AS Location,
                    is_deleted AS IsDeleted,
                    _last_sortable_unique_id AS LastSortableUniqueId
             FROM {MultiProviderFixtureBase.ForecastTable}
             WHERE forecast_id = @ForecastId;
             """,
            new { ForecastId = forecastId.ToString("D") }).ConfigureAwait(false);
        var registry = await connection.QuerySingleAsync<RegistryDbRow>(
            """
            SELECT applied_event_version AS AppliedEventVersion,
                   current_position AS CurrentPosition,
                   last_applied_source AS LastAppliedSource
            FROM sekiban_mv_registry
            WHERE view_name = 'WeatherForecastPortable'
              AND logical_table = 'forecasts';
            """).ConfigureAwait(false);
        var activeVersion = await connection.ExecuteScalarAsync<int>(
            """
            SELECT active_version
            FROM sekiban_mv_active
            WHERE view_name = 'WeatherForecastPortable';
            """).ConfigureAwait(false);

        Assert.Equal(3, firstCatchUp.AppliedEvents);
        Assert.Equal(0, secondCatchUp.AppliedEvents);
        Assert.Equal("Kyoto", row.Location);
        Assert.True(row.IsDeleted);
        Assert.False(string.IsNullOrWhiteSpace(row.LastSortableUniqueId));
        Assert.Equal(3, registry.AppliedEventVersion);
        Assert.Equal("catchup", registry.LastAppliedSource);
        Assert.False(string.IsNullOrWhiteSpace(registry.CurrentPosition));
        Assert.Equal(1, activeVersion);
    }
}
