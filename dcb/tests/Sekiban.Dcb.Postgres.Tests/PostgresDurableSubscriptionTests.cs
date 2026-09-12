using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
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

        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                "UPDATE dcb_durable_subscriptions SET next_attempt_at_utc = CURRENT_TIMESTAMP - INTERVAL '1 second' "
                + "WHERE service_id = 'default' AND subscription_name = 'lifecycle'");
        }

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
        Assert.Equal(DurableSubscriptionRunnerStatus.Standby, standby.GetValue().Status);
        Assert.Equal(DurableSubscriptionPhase.CatchingUp, standby.GetValue().State.Phase);

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

    [Fact]
    public async Task PostgresNowControlsOwnership_AndStandbyStatusDoesNotRewriteDurablePhase()
    {
        var store = CreateStore();
        var identity = new DurableSubscriptionIdentity("clock-service", "ownership");
        var initialized = await store.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        Assert.True(initialized.IsSuccess, initialized.IsSuccess ? string.Empty : initialized.GetException().ToString());

        var first = await store.TryAcquireAsync(identity, "owner-a", TimeSpan.FromSeconds(30));
        Assert.True(first.IsSuccess && first.GetValue().Acquired);
        Assert.Equal(DurableSubscriptionRunnerStatus.Owner, first.GetValue().Status);

        var standby = await store.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(standby.IsSuccess);
        Assert.False(standby.GetValue().Acquired);
        Assert.Equal(DurableSubscriptionRunnerStatus.Standby, standby.GetValue().Status);
        Assert.Equal(DurableSubscriptionPhase.CatchingUp, standby.GetValue().State.Phase);

        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                "UPDATE dcb_durable_subscriptions SET lease_expires_at_utc = CURRENT_TIMESTAMP - INTERVAL '1 second' "
                + "WHERE service_id = 'clock-service' AND subscription_name = 'ownership'");
        }

        var takeover = await store.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(takeover.IsSuccess && takeover.GetValue().Acquired);
        Assert.Equal(first.GetValue().State.OwnerGeneration + 1, takeover.GetValue().State.OwnerGeneration);
        Assert.Equal(DurableSubscriptionPhase.CatchingUp, (await store.ReadAsync(identity)).GetValue().Phase);
    }

    [Fact]
    public async Task FencedHaltPreservesFirstEvidenceAndPosition()
    {
        var eventId = await AppendEventAsync("halt-evidence");
        var store = CreateStore();
        var identity = new DurableSubscriptionIdentity("halt-service", "orders");
        var initialized = await store.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        Assert.True(initialized.IsSuccess, initialized.IsSuccess ? string.Empty : initialized.GetException().ToString());

        var first = await store.TryAcquireAsync(identity, "owner-a", TimeSpan.FromSeconds(30));
        Assert.True(first.IsSuccess && first.GetValue().Acquired);
        var firstGeneration = first.GetValue().State.OwnerGeneration;
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                "UPDATE dcb_durable_subscriptions SET lease_expires_at_utc = CURRENT_TIMESTAMP - INTERVAL '1 second' "
                + "WHERE service_id = 'halt-service' AND subscription_name = 'orders'");
        }

        var second = await store.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(second.IsSuccess && second.GetValue().Acquired);
        var secondGeneration = second.GetValue().State.OwnerGeneration;

        var staleHalt = await store.HaltAsync(identity, "owner-a", firstGeneration, "stale-halt");
        Assert.False(staleHalt.IsSuccess);
        var stillOwned = await store.ReadAsync(identity);
        Assert.True(stillOwned.IsSuccess);
        Assert.Equal("owner-b", stillOwned.GetValue().OwnerId);
        Assert.Equal(DurableSubscriptionPhase.CatchingUp, stillOwned.GetValue().Phase);

        var acknowledged = await store.AcknowledgeAsync(identity, "owner-b", secondGeneration, eventId);
        Assert.True(acknowledged.IsSuccess, acknowledged.IsSuccess ? string.Empty : acknowledged.GetException().ToString());
        var halted = await store.HaltAsync(identity, "owner-b", secondGeneration, "first-halt");
        Assert.True(halted.IsSuccess, halted.IsSuccess ? string.Empty : halted.GetException().ToString());
        Assert.Equal(eventId, halted.GetValue().LastHaltPosition);

        var repeated = await store.HaltAsync(identity, "owner-b", secondGeneration, "second-halt");
        Assert.False(repeated.IsSuccess);
        var final = await store.ReadAsync(identity);
        Assert.True(final.IsSuccess);
        Assert.Equal(DurableSubscriptionPhase.Halted, final.GetValue().Phase);
        Assert.Equal("first-halt", final.GetValue().LastHaltReason);
        Assert.Equal(eventId, final.GetValue().LastHaltPosition);
    }

    [Fact]
    public async Task RetryTimestampAndFailurePositionAreDatabaseGatedAcrossTakeover()
    {
        var firstPosition = await AppendEventAsync("retry-first", "retry-service");
        var secondPosition = await AppendEventAsync("retry-second", "retry-service");
        var store = CreateStore();
        var identity = new DurableSubscriptionIdentity("retry-service", "orders");
        var initialized = await store.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        Assert.True(initialized.IsSuccess, initialized.IsSuccess ? string.Empty : initialized.GetException().ToString());

        var first = await store.TryAcquireAsync(identity, "owner-a", TimeSpan.FromSeconds(30));
        Assert.True(first.IsSuccess && first.GetValue().Acquired);
        var firstFailure = await store.RecordFailureAsync(
            identity, "owner-a", first.GetValue().State.OwnerGeneration, firstPosition, "transient",
            conversionFailure: false, maxHandlerAttempts: 4, retryDelay: TimeSpan.FromMinutes(1));
        Assert.True(firstFailure.IsSuccess, firstFailure.IsSuccess ? string.Empty : firstFailure.GetException().ToString());
        Assert.Equal(1, firstFailure.GetValue().ActiveFailureCount);
        Assert.Equal(DurableSubscriptionPhase.Retrying, firstFailure.GetValue().Phase);

        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                "UPDATE dcb_durable_subscriptions SET lease_expires_at_utc = CURRENT_TIMESTAMP - INTERVAL '1 second' "
                + "WHERE service_id = 'retry-service' AND subscription_name = 'orders'");
        }

        var takeover = await store.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(takeover.IsSuccess && takeover.GetValue().Acquired);
        Assert.Equal(DurableSubscriptionPhase.Retrying, takeover.GetValue().State.Phase);
        Assert.False((await store.IsRetryDueAsync(
            identity, "owner-b", takeover.GetValue().State.OwnerGeneration)).GetValue());

        var earlyFailure = await store.RecordFailureAsync(
            identity, "owner-b", takeover.GetValue().State.OwnerGeneration, firstPosition, "too-early",
            conversionFailure: false, maxHandlerAttempts: 4, retryDelay: TimeSpan.FromMinutes(1));
        Assert.False(earlyFailure.IsSuccess);
        var earlyAck = await store.AcknowledgeAsync(
            identity, "owner-b", takeover.GetValue().State.OwnerGeneration, secondPosition);
        Assert.False(earlyAck.IsSuccess);

        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                "UPDATE dcb_durable_subscriptions SET next_attempt_at_utc = CURRENT_TIMESTAMP - INTERVAL '1 second' "
                + "WHERE service_id = 'retry-service' AND subscription_name = 'orders'");
        }

        var differentPosition = await store.RecordFailureAsync(
            identity, "owner-b", takeover.GetValue().State.OwnerGeneration, secondPosition, "new-position",
            conversionFailure: false, maxHandlerAttempts: 4, retryDelay: TimeSpan.FromMinutes(1));
        Assert.True(differentPosition.IsSuccess, differentPosition.IsSuccess ? string.Empty : differentPosition.GetException().ToString());
        Assert.Equal(1, differentPosition.GetValue().ActiveFailureCount);
        Assert.Equal(secondPosition, differentPosition.GetValue().ActiveFailurePosition);
    }

    [Fact]
    public void MixedProvisioningModesFailClosedInEitherRegistrationOrder()
    {
        var legacyThenPreProvisioned = new ServiceCollection();
        legacyThenPreProvisioned.AddSekibanDcbPostgresDurableSubscription(
            options => options.Name = "legacy",
            (_, _) => Task.CompletedTask);
        Assert.Throws<InvalidOperationException>(() =>
            legacyThenPreProvisioned.AddSekibanDcbPostgresDurableSubscription(
                options =>
                {
                    options.Name = "pre-provisioned";
                    options.ProvisioningMode = DurableSubscriptionProvisioningMode.PreProvisioned;
                },
                (_, _) => Task.CompletedTask));

        var preProvisionedThenLegacy = new ServiceCollection();
        preProvisionedThenLegacy.AddSekibanDcbPostgresDurableSubscription(
            options =>
            {
                options.Name = "pre-provisioned";
                options.ProvisioningMode = DurableSubscriptionProvisioningMode.PreProvisioned;
            },
            (_, _) => Task.CompletedTask);
        Assert.Throws<InvalidOperationException>(() =>
            preProvisionedThenLegacy.AddSekibanDcbPostgresDurableSubscription(
                options => options.Name = "legacy",
                (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task RestartTakeoverAndReentrantAcquirePreserveTheDurableCursor()
    {
        var eventId = await AppendEventAsync("restart", "restart-service");
        var identity = new DurableSubscriptionIdentity("restart-service", "orders");
        var firstStore = CreateStore();
        var initialized = await firstStore.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        Assert.True(initialized.IsSuccess, initialized.IsSuccess ? string.Empty : initialized.GetException().ToString());
        var first = await firstStore.TryAcquireAsync(identity, "owner-a", TimeSpan.FromSeconds(30));
        Assert.True(first.IsSuccess && first.GetValue().Acquired);
        var acknowledged = await firstStore.AcknowledgeAsync(
            identity, "owner-a", first.GetValue().State.OwnerGeneration, eventId);
        Assert.True(acknowledged.IsSuccess);

        var restartedStore = CreateStore();
        var afterRestart = await restartedStore.ReadAsync(identity);
        Assert.True(afterRestart.IsSuccess);
        Assert.Equal(eventId, afterRestart.GetValue().AcknowledgedPosition);

        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                "UPDATE dcb_durable_subscriptions SET lease_expires_at_utc = CURRENT_TIMESTAMP - INTERVAL '1 second' "
                + "WHERE service_id = 'restart-service' AND subscription_name = 'orders'");
        }

        var takeover = await restartedStore.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(takeover.IsSuccess && takeover.GetValue().Acquired);
        var generation = takeover.GetValue().State.OwnerGeneration;
        var reentrant = await restartedStore.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(reentrant.IsSuccess && reentrant.GetValue().Acquired);
        Assert.Equal(generation, reentrant.GetValue().State.OwnerGeneration);
        Assert.Equal(eventId, reentrant.GetValue().State.AcknowledgedPosition);
    }

    [Fact]
    public async Task ConversionPoisonHaltsWithoutSkippingAndHaltedRunnerReReads()
    {
        var position = await AppendEventAsync("poison", "poison-service");
        var store = CreateStore();
        var identity = new DurableSubscriptionIdentity("poison-service", "orders");
        var initialized = await store.InitializeOrGetAsync(
            identity,
            DurableSubscriptionStartPolicy.FromBeginning,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.Empty));
        Assert.True(initialized.IsSuccess, initialized.IsSuccess ? string.Empty : initialized.GetException().ToString());
        var lease = await store.TryAcquireAsync(identity, "owner-a", TimeSpan.FromSeconds(30));
        Assert.True(lease.IsSuccess && lease.GetValue().Acquired);
        var halted = await store.RecordFailureAsync(
            identity, "owner-a", lease.GetValue().State.OwnerGeneration, position, "conversion-poison",
            conversionFailure: true, maxHandlerAttempts: 4, retryDelay: TimeSpan.FromSeconds(1));
        Assert.True(halted.IsSuccess, halted.IsSuccess ? string.Empty : halted.GetException().ToString());
        Assert.Equal(DurableSubscriptionPhase.Halted, halted.GetValue().Phase);
        Assert.Null(halted.GetValue().AcknowledgedPosition);

        var reacquire = await store.TryAcquireAsync(identity, "owner-b", TimeSpan.FromSeconds(30));
        Assert.True(reacquire.IsSuccess);
        Assert.False(reacquire.GetValue().Acquired);
        Assert.Equal(DurableSubscriptionRunnerStatus.Halted, reacquire.GetValue().Status);

        var handlerCalls = 0;
        var nudge = new RecordingNudgeFactory();
        var runner = CreateRunner(
            store,
            identity,
            nudge,
            (_, _) =>
            {
                Interlocked.Increment(ref handlerCalls);
                return Task.CompletedTask;
            });
        using var cancellation = new CancellationTokenSource();
        await runner.StartAsync(cancellation.Token);
        await WaitUntilAsync(() => Task.FromResult(runner.Status == DurableSubscriptionRunnerStatus.Halted));
        await nudge.TriggerAsync();
        Assert.Equal(0, handlerCalls);
        var final = await store.ReadAsync(identity);
        Assert.Equal(DurableSubscriptionPhase.Halted, final.GetValue().Phase);
        cancellation.Cancel();
        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task IdleDuplicateAndOutOfOrderNudgesReuseLeaseAndCatchUpPromptly()
    {
        var firstPosition = await AppendEventAsync("idle-first", "idle-service");
        var store = CreateStore();
        var identity = new DurableSubscriptionIdentity("idle-service", "orders");
        var nudge = new RecordingNudgeFactory();
        var firstHandled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHandled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var secondPosition = string.Empty;
        var runner = CreateRunner(
            store,
            identity,
            nudge,
            (eventRecord, _) =>
            {
                Interlocked.Increment(ref calls);
                if (eventRecord.SortableUniqueIdValue == firstPosition)
                {
                    firstHandled.TrySetResult(eventRecord.SortableUniqueIdValue);
                }
                else if (eventRecord.SortableUniqueIdValue == secondPosition)
                {
                    secondHandled.TrySetResult(eventRecord.SortableUniqueIdValue);
                }

                return Task.CompletedTask;
            });
        using var cancellation = new CancellationTokenSource();
        await runner.StartAsync(cancellation.Token);
        Assert.Equal(firstPosition, await firstHandled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await WaitUntilAsync(async () => (await store.ReadAsync(identity)).GetValue().Phase == DurableSubscriptionPhase.Idle);
        var before = (await store.ReadAsync(identity)).GetValue();

        await nudge.TriggerAsync();
        await nudge.TriggerAsync();
        await WaitUntilAsync(async () =>
        {
            var observed = await store.ReadAsync(identity);
            return observed.IsSuccess
                && observed.GetValue().Phase == DurableSubscriptionPhase.Idle
                && observed.GetValue().UpdatedAtUtc > before.UpdatedAtUtc;
        });
        Assert.Equal(1, calls);

        secondPosition = await AppendEventAsync("idle-second", "idle-service");
        await nudge.TriggerAsync();
        Assert.Equal(secondPosition, await secondHandled.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        await WaitUntilAsync(async () =>
        {
            var observed = await store.ReadAsync(identity);
            return observed.IsSuccess && observed.GetValue().AcknowledgedPosition == secondPosition;
        });
        var after = await store.ReadAsync(identity);
        Assert.True(after.IsSuccess);
        Assert.Equal(before.OwnerGeneration, after.GetValue().OwnerGeneration);
        Assert.Equal(secondPosition, after.GetValue().AcknowledgedPosition);
        Assert.Equal(2, calls);

        cancellation.Cancel();
        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LongHandlerFenceLossCancelsBeforeAcknowledgement()
    {
        var position = await AppendEventAsync("long-handler", "fence-runner-service");
        var store = CreateStore();
        var identity = new DurableSubscriptionIdentity("fence-runner-service", "orders");
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = CreateRunner(
            store,
            identity,
            new RecordingNudgeFactory(),
            async (_, token) =>
            {
                entered.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult(true);
                    throw;
                }
            },
            leaseDuration: TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        await runner.StartAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var owned = await store.ReadAsync(identity);
        Assert.Null(owned.GetValue().AcknowledgedPosition);
        Assert.NotNull(owned.GetValue().OwnerId);

        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection,
                "UPDATE dcb_durable_subscriptions SET owner_id = 'fenced-out', "
                + "owner_generation = owner_generation + 1, lease_expires_at_utc = CURRENT_TIMESTAMP + INTERVAL '30 seconds' "
                + "WHERE service_id = 'fence-runner-service' AND subscription_name = 'orders'");
        }

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var after = await store.ReadAsync(identity);
        Assert.True(after.IsSuccess);
        Assert.Null(after.GetValue().AcknowledgedPosition);
        cancellation.Cancel();
        await runner.StopAsync(CancellationToken.None);
    }

    private PostgresDurableSubscriptionStore CreateStore() =>
        new(Fixture.DbContextFactory, legacyAutoProvision: false);

    private DurableSubscriptionRunner CreateRunner(
        IDurableSubscriptionStore store,
        DurableSubscriptionIdentity identity,
        RecordingNudgeFactory nudge,
        DurableSubscriptionHandler handler,
        TimeSpan? leaseDuration = null)
    {
        var registration = new DurableSubscriptionRegistration(
            new DurableSubscriptionOptions
            {
                ServiceId = identity.ServiceId,
                Name = identity.Name,
                StartPolicy = DurableSubscriptionStartPolicy.FromBeginning,
                SafeWindow = TimeSpan.Zero,
                PollInterval = TimeSpan.FromMilliseconds(25),
                RetryDelay = TimeSpan.FromMilliseconds(25),
                LeaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30),
                MaxHandlerAttempts = 4
            },
            handler);
        return new DurableSubscriptionRunner(
            registration,
            store,
            new PostgresEventStoreFactory(Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes),
            Fixture.DomainTypes.EventTypes,
            nudge,
            NullLogger<DurableSubscriptionRunner>.Instance);
    }

    private async Task<string> AppendEventAsync(string marker, string serviceId = "default")
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
        var eventStore = new PostgresEventStoreFactory(Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes)
            .CreateForService(serviceId);
        var written = await eventStore.WriteSerializableEventsAsync([serialized]);
        Assert.True(written.IsSuccess, written.IsSuccess ? string.Empty : written.GetException().ToString());
        return eventId;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
        }

        Assert.True(await condition().ConfigureAwait(false), "The expected durable PostgreSQL state was not observed before the bounded deadline.");
    }

    private sealed class RecordingNudgeFactory : IDurableSubscriptionNudgeFactory
    {
        private Func<ValueTask>? _onNudge;

        public IDurableSubscriptionNudge Create(DurableSubscriptionIdentity identity, Func<ValueTask> onNudge)
        {
            _onNudge = onNudge;
            return new RecordingNudge();
        }

        public ValueTask TriggerAsync() => _onNudge?.Invoke() ?? ValueTask.CompletedTask;
    }

    private sealed class RecordingNudge : IDurableSubscriptionNudge
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
