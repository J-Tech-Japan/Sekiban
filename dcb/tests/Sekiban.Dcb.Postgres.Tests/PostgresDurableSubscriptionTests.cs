using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Dcb.Domain.Weather;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Subscriptions;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
/// Real PostgreSQL proofs for the opt-in durable subscriber state boundary. These tests intentionally exercise the
/// provider through its public store registration surface rather than replacing PostgreSQL with a dictionary fake.
/// </summary>
public sealed class PostgresDurableSubscriptionTests : PostgresTestBase
{
    public PostgresDurableSubscriptionTests(PostgresTestFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "DROP TABLE IF EXISTS dcb_durable_subscriptions");
        await Fixture.DbContextFactory.ProvisionDurableSubscriptionSchemaAsync();
    }

    [Fact]
    public void Options_ValidateBoundsAndIdentityWithoutChangingLegacyCallbackContracts()
    {
        var options = new DurableSubscriptionOptions
        {
            ServiceId = "durable-service",
            Name = "  orders  ",
            SafeWindow = TimeSpan.FromMinutes(1),
            LeaseDuration = TimeSpan.FromSeconds(2),
            PollInterval = TimeSpan.FromSeconds(1),
            RetryDelay = TimeSpan.FromSeconds(1),
            MaxHandlerAttempts = 4,
            MaxBatchSize = 10
        };

        options.Validate();

        Assert.Equal("durable-service", options.Identity.ServiceId);
        Assert.Equal("orders", options.Identity.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DurableSubscriptionOptions { MaxBatchSize = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DurableSubscriptionOptions { MaxHandlerAttempts = 17 }.Validate());
        Assert.Throws<ArgumentException>(() =>
            new DurableSubscriptionIdentity("service", "bad\nname"));
    }

    [Fact]
    public async Task InitializeOrGet_UsesSafeTail_FromNowAndPreservesExistingCursor()
    {
        var eventId = await AppendEventAsync("from-now");
        var identity = new DurableSubscriptionIdentity("default", "from-now");
        var safeTail = SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-1), Guid.Empty);
        var store = new PostgresDurableSubscriptionStore(Fixture.DbContextFactory, legacyAutoProvision: false);

        var first = await store.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromNow,
            safeTail);
        var second = await store.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));

        Assert.True(first.IsSuccess, first.IsSuccess ? string.Empty : first.GetException().ToString());
        Assert.True(second.IsSuccess, second.IsSuccess ? string.Empty : second.GetException().ToString());
        Assert.Equal(safeTail, first.GetValue().AcknowledgedPosition);
        Assert.Equal(first.GetValue().AcknowledgedPosition, second.GetValue().AcknowledgedPosition);
        Assert.Equal(DurableSubscriptionPhase.CatchingUp, first.GetValue().Phase);
        Assert.True(string.CompareOrdinal(first.GetValue().AcknowledgedPosition, eventId) < 0);
    }

    [Fact]
    public async Task InitializeOrGet_EmptyStoreUsesNullCursor_ForBothStartPolicies()
    {
        var store = new PostgresDurableSubscriptionStore(Fixture.DbContextFactory, legacyAutoProvision: false);
        var fromNow = await store.InitializeOrGetAsync(
            new DurableSubscriptionIdentity("default", "empty-now"),
            DurableSubscriptionStartPolicy.FromNow,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        var fromBeginning = await store.InitializeOrGetAsync(
            new DurableSubscriptionIdentity("default", "empty-beginning"),
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));

        Assert.True(fromNow.IsSuccess, fromNow.IsSuccess ? string.Empty : fromNow.GetException().ToString());
        Assert.True(fromBeginning.IsSuccess, fromBeginning.IsSuccess ? string.Empty : fromBeginning.GetException().ToString());
        Assert.Null(fromNow.GetValue().AcknowledgedPosition);
        Assert.Null(fromBeginning.GetValue().AcknowledgedPosition);
    }

    [Fact]
    public async Task PreProvisionedRuntime_IsDmlOnlyAndMissingTableReturnsProviderFailure()
    {
        var store = new PostgresDurableSubscriptionStore(Fixture.DbContextFactory, legacyAutoProvision: false);
        var identity = new DurableSubscriptionIdentity("default", "missing-table");

        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, "DROP TABLE dcb_durable_subscriptions");
        }

        try
        {
            var missing = await store.ReadAsync(identity);
            Assert.False(missing.IsSuccess);
            var failure = Assert.IsType<PostgresException>(missing.GetException());
            Assert.Equal(PostgresErrorCodes.UndefinedTable, failure.SqlState);
        }
        finally
        {
            await Fixture.DbContextFactory.ProvisionDurableSubscriptionSchemaAsync();
        }

        var initialized = await store.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        Assert.True(initialized.IsSuccess, initialized.IsSuccess ? string.Empty : initialized.GetException().ToString());
    }

    [Fact]
    public async Task PreProvisionedRuntime_MissingColumnReturns42703InsteadOfAutoRepair()
    {
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, "ALTER TABLE dcb_durable_subscriptions DROP COLUMN phase");
        }

        try
        {
            var store = new PostgresDurableSubscriptionStore(Fixture.DbContextFactory, legacyAutoProvision: false);
            var result = await store.ReadAsync(new DurableSubscriptionIdentity("default", "missing-column"));
            Assert.False(result.IsSuccess);
            var failure = Assert.IsType<PostgresException>(result.GetException());
            Assert.Equal(PostgresErrorCodes.UndefinedColumn, failure.SqlState);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
            await connection.OpenAsync();
            await ExecuteAsync(connection, "DROP TABLE dcb_durable_subscriptions");
            await Fixture.DbContextFactory.ProvisionDurableSubscriptionSchemaAsync();
        }
    }

    [Fact]
    public async Task AcquireRenewAcknowledgeFailureHaltAndResume_PersistStateAndFenceOwner()
    {
        var eventId = await AppendEventAsync("lifecycle");
        var identity = new DurableSubscriptionIdentity("default", "lifecycle");
        var store = new PostgresDurableSubscriptionStore(Fixture.DbContextFactory, legacyAutoProvision: false);
        var initialized = await store.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        Assert.True(initialized.IsSuccess, initialized.IsSuccess ? string.Empty : initialized.GetException().ToString());

        var lease = await store.TryAcquireAsync(identity, "owner-a", TimeSpan.FromSeconds(30));
        Assert.True(lease.IsSuccess, lease.IsSuccess ? string.Empty : lease.GetException().ToString());
        Assert.True(lease.GetValue().Acquired);
        var generation = lease.GetValue().State.OwnerGeneration;

        var renewed = await store.RenewAsync(identity, "owner-a", generation, TimeSpan.FromSeconds(30));
        Assert.True(renewed.IsSuccess && renewed.GetValue());

        var acknowledged = await store.AcknowledgeAsync(identity, "owner-a", generation, eventId);
        Assert.True(acknowledged.IsSuccess, acknowledged.IsSuccess ? string.Empty : acknowledged.GetException().ToString());
        Assert.Equal(eventId, acknowledged.GetValue().AcknowledgedPosition);

        var retry = await store.RecordFailureAsync(
            identity, "owner-a", generation, eventId, "transient", conversionFailure: false,
            maxHandlerAttempts: 3, retryDelay: TimeSpan.FromSeconds(1));
        Assert.True(retry.IsSuccess, retry.IsSuccess ? string.Empty : retry.GetException().ToString());
        Assert.Equal(DurableSubscriptionPhase.Retrying, retry.GetValue().Phase);
        Assert.Equal(1, retry.GetValue().ActiveFailureCount);

        var halted = await store.RecordFailureAsync(
            identity, "owner-a", generation, eventId, "poison", conversionFailure: false,
            maxHandlerAttempts: 2, retryDelay: TimeSpan.FromSeconds(1));
        Assert.True(halted.IsSuccess, halted.IsSuccess ? string.Empty : halted.GetException().ToString());
        Assert.Equal(DurableSubscriptionPhase.Halted, halted.GetValue().Phase);
        Assert.Equal(eventId, halted.GetValue().LastHaltPosition);
        Assert.Null(halted.GetValue().OwnerId);

        var resumed = await store.ResumeAsync(identity);
        Assert.True(resumed.IsSuccess, resumed.IsSuccess ? string.Empty : resumed.GetException().ToString());
        Assert.Equal(DurableSubscriptionPhase.CatchingUp, resumed.GetValue().Phase);
        Assert.Equal(eventId, resumed.GetValue().AcknowledgedPosition);
        Assert.Null(resumed.GetValue().OwnerId);
        Assert.True(resumed.GetValue().OwnerGeneration > halted.GetValue().OwnerGeneration);

        var standby = await store.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(standby.IsSuccess, standby.IsSuccess ? string.Empty : standby.GetException().ToString());
        Assert.True(standby.GetValue().Acquired);
    }

    [Fact]
    public async Task LeaseUsesDatabaseTime_StandbyTakeoverAndStaleGenerationAckAreRejected()
    {
        var eventId = await AppendEventAsync("fence");
        var identity = new DurableSubscriptionIdentity("fence-service", "orders");
        var store = new PostgresDurableSubscriptionStore(Fixture.DbContextFactory, legacyAutoProvision: false);
        var initialized = await store.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        Assert.True(initialized.IsSuccess, initialized.IsSuccess ? string.Empty : initialized.GetException().ToString());

        var first = await store.TryAcquireAsync(identity, "owner-a", TimeSpan.FromSeconds(30));
        Assert.True(first.IsSuccess && first.GetValue().Acquired);
        var firstGeneration = first.GetValue().State.OwnerGeneration;

        var standby = await store.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(standby.IsSuccess, standby.IsSuccess ? string.Empty : standby.GetException().ToString());
        Assert.False(standby.GetValue().Acquired);
        Assert.Equal(DurableSubscriptionPhase.Standby, standby.GetValue().State.Phase);

        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                "UPDATE dcb_durable_subscriptions SET lease_expires_at_utc = CURRENT_TIMESTAMP - INTERVAL '1 second' "
                + "WHERE service_id = 'fence-service' AND subscription_name = 'orders'");
        }

        var takeover = await store.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(takeover.IsSuccess && takeover.GetValue().Acquired);
        Assert.True(takeover.GetValue().State.OwnerGeneration > firstGeneration);

        var staleAck = await store.AcknowledgeAsync(identity, "owner-a", firstGeneration, eventId);
        Assert.False(staleAck.IsSuccess);
        Assert.Contains("lease was lost", staleAck.GetException().Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ServiceIdentityIsolated_DurableRegistrationRejectsOnlyInOneContainer()
    {
        var store = new PostgresDurableSubscriptionStore(Fixture.DbContextFactory, legacyAutoProvision: false);
        var first = await store.InitializeOrGetAsync(
            new DurableSubscriptionIdentity("service-a", "orders"),
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        var second = await store.InitializeOrGetAsync(
            new DurableSubscriptionIdentity("service-b", "orders"),
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        Assert.True(first.IsSuccess && second.IsSuccess);

        var firstContainer = new ServiceCollection();
        firstContainer.AddSekibanDcbPostgresDurableSubscription(
            options =>
            {
                options.ServiceId = "service-a";
                options.Name = "orders";
                options.ProvisioningMode = DurableSubscriptionProvisioningMode.PreProvisioned;
            },
            (_, _) => Task.CompletedTask);
        Assert.Throws<InvalidOperationException>(() => firstContainer.AddSekibanDcbPostgresDurableSubscription(
            options =>
            {
                options.ServiceId = "service-a";
                options.Name = "orders";
            },
            (_, _) => Task.CompletedTask));

        var secondContainer = new ServiceCollection();
        secondContainer.AddSekibanDcbPostgresDurableSubscription(
            options =>
            {
                options.ServiceId = "service-a";
                options.Name = "orders";
            },
            (_, _) => Task.CompletedTask);
        Assert.NotEmpty(secondContainer);
    }

    [Fact]
    public async Task DifferentDurableIdentitiesResolveTheirOwnHostedRunners()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Fixture.DbContextFactory);
        services.AddSingleton<IEventTypes>(Fixture.DomainTypes.EventTypes);
        services.AddSingleton<IEventStoreFactory>(
            new PostgresEventStoreFactory(Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes));
        services.AddSingleton<IDurableSubscriptionStore>(
            new PostgresDurableSubscriptionStore(Fixture.DbContextFactory, legacyAutoProvision: false));
        services.AddSekibanDcbPostgresDurableSubscription(
            options => options.Name = "first",
            (_, _) => Task.CompletedTask);
        services.AddSekibanDcbPostgresDurableSubscription(
            options => options.Name = "second",
            (_, _) => Task.CompletedTask);

        await using var provider = services.BuildServiceProvider();
        var runners = provider.GetServices<DurableSubscriptionRunner>().ToArray();
        Assert.Equal(2, runners.Length);
        Assert.Equal(new[] { "first", "second" }, runners.Select(runner => runner.Identity.Name).OrderBy(name => name));

        var handles = provider.GetServices<IDurableSubscriptionHandle>().ToArray();
        Assert.Equal(2, handles.Length);
        Assert.Equal(new[] { "first", "second" }, handles.Select(handle => handle.Identity.Name).OrderBy(name => name));

        var hostedRunners = provider.GetServices<IHostedService>().OfType<DurableSubscriptionRunner>().ToArray();
        Assert.Equal(2, hostedRunners.Length);
        Assert.Equal(new[] { "first", "second" }, hostedRunners.Select(runner => runner.Identity.Name).OrderBy(name => name));
    }

    private async Task<string> AppendEventAsync(string marker)
    {
        var eventId = SortableUniqueId.GenerateNew();
        var payload = new WeatherForecastCreated(
            Guid.CreateVersion7(), marker, DateOnly.FromDateTime(DateTime.UtcNow), 20, marker);
        var serialized = new Event(
                payload,
                eventId,
                nameof(WeatherForecastCreated),
                Guid.CreateVersion7(),
                new EventMetadata("durable-subscription", marker, "test-user"),
                [])
            .ToSerializableEvent(Fixture.DomainTypes.EventTypes);
        var written = await Fixture.EventStore.WriteSerializableEventsAsync([serialized]);
        Assert.True(written.IsSuccess, written.IsSuccess ? string.Empty : written.GetException().ToString());
        return eventId;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
