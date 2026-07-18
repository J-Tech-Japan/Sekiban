using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 Postgres conditional (unique-key) append against a real PostgreSQL container (Testcontainers). Beyond the
///     shared uniform contract this covers genuine cross-transaction concurrency (same-operation AND different-operation
///     races) and — the Fix #3 requirement — constraint-name discrimination: only a 23505 on the events-table primary key
///     <c>PK_dcb_events</c> is the claim collision; an unrelated unique violation preserves its original provider failure
///     and is never misrouted to winner classification.
/// </summary>
public class PostgresConditionalAppendTests : PostgresTestBase
{
    public PostgresConditionalAppendTests(PostgresTestFixture fixture) : base(fixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        ConditionalAppendScenarios.RegisterMarker(Fixture.DomainTypes);
    }

    private IConditionalEventStore Conditional => (IConditionalEventStore)Fixture.EventStore;

    private async Task<int> DurableCount() =>
        (await Fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue().Count();

    private SerializableEvent MarkerFixed(string value, string sortableId) =>
        new Event(new ConditionalMarkerEvent(value), sortableId, nameof(ConditionalMarkerEvent),
                Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), new List<string> { "Migration:once" })
            .ToSerializableEvent(Fixture.DomainTypes.EventTypes);

    [Fact]
    public void Capability_ReportsSingleEventUniqueKey() =>
        ConditionalAppendScenarios.AssertCapability(
            (Sekiban.Dcb.Capabilities.IWriteConditionCapabilityProvider)Fixture.EventStore);

    [Fact]
    public Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent() =>
        ConditionalAppendScenarios.AssertFirstAppendWins_SameOpRetryIsIdempotent(
            Conditional, Fixture.DomainTypes, "pg-1", DurableCount);

    [Fact]
    public Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause() =>
        ConditionalAppendScenarios.AssertDifferentOperationIsKeyReuseConflict_WithProviderCause(
            Conditional, Fixture.DomainTypes, "pg-2", DurableCount);

    [Fact]
    public Task NWriters_SameOperation_ConcurrentTransactions_OneAppended_RestAlreadyCommitted_OneDurableEvent() =>
        ConditionalAppendScenarios.AssertNWritersConverge(Conditional, Fixture.DomainTypes, "pg-race", 10, DurableCount);

    [Fact]
    public async Task ConcurrentDifferentOperations_SameKey_OneWins_OtherIsKeyReuseConflict()
    {
        var results = await Task.WhenAll(
            Conditional.AppendIfUniqueAsync(new ConditionalAppendRequest("pg-diff", ConditionalAppendScenarios.Marker(Fixture.DomainTypes, "A"))),
            Conditional.AppendIfUniqueAsync(new ConditionalAppendRequest("pg-diff", ConditionalAppendScenarios.Marker(Fixture.DomainTypes, "B"))));

        Assert.Equal(1, results.Count(r => r.IsSuccess && r.GetValue().Status == ConditionalAppendStatus.Appended));
        Assert.Equal(1, results.Count(r => !r.IsSuccess && r.GetException() is KeyReuseConflictException));
        Assert.Equal(1, await DurableCount());
    }

    [Fact]
    public async Task CancelledMidWrite_IsTypedInDoubt_NoDurableEvent_ThenRetryConverges()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // SaveChanges is cancelled before commit: nothing durable, and the outcome resolves by authoritative read-back
        // (no winner) to a typed retryable in-doubt.
        var cancelled = await Conditional.AppendIfUniqueAsync(
            new ConditionalAppendRequest("pg-cancel", ConditionalAppendScenarios.Marker(Fixture.DomainTypes, "v")), cts.Token);

        Assert.False(cancelled.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(cancelled.GetException());
        Assert.True(ex.IsRetryable);
        Assert.Equal(0, await DurableCount()); // transaction rolled back — nothing committed

        // Recovery: a normal retry converges to a durable Appended.
        var retry = (await Conditional.AppendIfUniqueAsync(
            new ConditionalAppendRequest("pg-cancel", ConditionalAppendScenarios.Marker(Fixture.DomainTypes, "v")))).GetValue();
        Assert.Equal(ConditionalAppendStatus.Appended, retry.Status);
        Assert.Equal(1, await DurableCount());
    }

    [Fact]
    public async Task PostCommitResponseLoss_Transport_FirstCallAmbiguous_RetryConvergesToAlreadyCommitted()
    {
        // TRUE post-commit ambiguity on the real container (distinct from the cancelled-before-commit rollback test): the
        // transaction commits durably, then the response is lost via a transport exception. First call ambiguous; a retry
        // reads the committed winner and converges to AlreadyCommitted — no second event.
        var transport = new InvalidOperationException("connection reset after commit");
        var store = (PostgresEventStore)Fixture.EventStore;
        store.AfterConditionalCommitHook = () => throw transport;
        try
        {
            var first = await Conditional.AppendIfUniqueAsync(
                new ConditionalAppendRequest("pg-postcommit", ConditionalAppendScenarios.Marker(Fixture.DomainTypes, "v")));
            Assert.False(first.IsSuccess);
            Assert.Same(transport, first.GetException());   // first call surfaces the ORIGINAL transport failure
            Assert.Equal(1, await DurableCount());           // but the write IS durable
        }
        finally
        {
            store.AfterConditionalCommitHook = null;
        }

        // Retry through a GENUINELY FRESH PostgresEventStore/DbContext over the same database (default ServiceId).
        var freshStore = new PostgresEventStore(
            Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes, new DefaultServiceIdProvider());
        var deterministicId = ConditionalAppendIdentity.DeriveEventId("default", OperationFingerprint.NormalizeKey("pg-postcommit"));
        var winner = (await freshStore.ReadSerializableEventAsync(deterministicId)).GetValue();
        var retry = (await ((IConditionalEventStore)freshStore).AppendIfUniqueAsync(
            new ConditionalAppendRequest("pg-postcommit", ConditionalAppendScenarios.Marker(Fixture.DomainTypes, "v")))).GetValue();

        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, retry.Status);
        ConditionalAppendScenarios.AssertReceiptMatchesStoredWinner(retry, "default", "pg-postcommit", Fixture.DomainTypes, winner);
        Assert.Equal(1, await DurableCount());         // no second event
    }

    [Fact]
    public void ResponseLossSeam_IsNonPublicInstance_NotStatic_AbsentFromInterfaces_AndUnsetInProduction()
    {
        // Structural guard: the post-commit response-loss seam cannot become a public/production-reachable mutation surface.
        const string hook = "AfterConditionalCommitHook";
        Assert.NotNull(typeof(PostgresEventStore).GetProperty(hook, BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Null(typeof(PostgresEventStore).GetProperty(hook, BindingFlags.Instance | BindingFlags.Public));
        Assert.Null(typeof(PostgresEventStore).GetProperty(hook, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
        foreach (var contract in new[] { typeof(IEventStore), typeof(IConditionalEventStore), typeof(IHotEventStore) })
        {
            Assert.Null(contract.GetProperty(hook));
        }

        // A store built the way production builds it (from the DbContext factory) carries no hook — construction touches no DB.
        var store = new PostgresEventStore(Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes, new DefaultServiceIdProvider());
        Assert.Null(store.AfterConditionalCommitHook);
    }

    [Fact]
    public async Task UnrelatedUniqueViolation_IsProviderFailure_NotAClaimConflict()
    {
        // Add a temporary UNRELATED unique index on (ServiceId, SortableUniqueId). A second conditional append that reuses
        // that sortable id under a DIFFERENT key derives a different deterministic EventId (so PK_dcb_events is fine) but
        // violates this unrelated constraint. Its 23505 carries a different ConstraintName, so it must surface as a
        // provider failure — never AlreadyCommitted or KeyReuseConflict.
        const string indexName = "ux_g16_unrelated_sortable";
        await using (var ctx = await Fixture.GetDbContextAsync())
        {
            await ctx.Database.ExecuteSqlRawAsync(
                $"CREATE UNIQUE INDEX {indexName} ON dcb_events (\"ServiceId\", \"SortableUniqueId\");");
        }

        try
        {
            var sortable = SortableUniqueId.GenerateNew();
            var first = await Conditional.AppendIfUniqueAsync(new ConditionalAppendRequest("pg-unrelated-A", MarkerFixed("A", sortable)));
            Assert.True(first.IsSuccess);
            Assert.Equal(ConditionalAppendStatus.Appended, first.GetValue().Status);

            var clash = await Conditional.AppendIfUniqueAsync(new ConditionalAppendRequest("pg-unrelated-B", MarkerFixed("B", sortable)));

            Assert.False(clash.IsSuccess);
            Assert.IsNotType<KeyReuseConflictException>(clash.GetException());
            Assert.IsNotType<ConditionalAppendInDoubtException>(clash.GetException());
            // The failure preserves its provider origin (a DbUpdateException wrapping the unrelated PostgresException).
            Assert.IsType<DbUpdateException>(clash.GetException());
        }
        finally
        {
            await using var ctx = await Fixture.GetDbContextAsync();
            await ctx.Database.ExecuteSqlRawAsync($"DROP INDEX IF EXISTS {indexName};");
        }
    }
}
