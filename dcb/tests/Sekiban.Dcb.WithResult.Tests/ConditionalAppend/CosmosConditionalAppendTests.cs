using Dcb.Domain;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Tags;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.TestSupport;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 Cosmos DB conditional (unique-key) append, end to end through the real <see cref="CosmosDbEventStore" />
///     against in-memory Cosmos containers. Cosmos writes the event document and its tag rows in SEPARATE phases, so the
///     headline coverage here is the crash-window/repair gate: a same-operation retry after an event-only partial commit
///     must idempotently repair every required tag row (proving exact tag-scoped visibility) BEFORE returning
///     AlreadyCommitted, and must surface a typed retryable in-doubt when the committed state cannot be reached.
/// </summary>
public class CosmosConditionalAppendTests
{
    private const string ServiceId = "svc";
    private readonly DcbDomainTypes _domain = ConditionalAppendScenarios.RegisterMarker(DomainType.GetDomainTypes());

    private sealed class Lineage
    {
        public Lineage(DcbDomainTypes domain, InMemoryCosmosClient client, CosmosDbEventStoreOptions options)
        {
            Client = client;
            Options = options;
            var context = new CosmosDbContext(client, "test-db", null, options);
            var resolver = new DefaultCosmosContainerResolver(options);
            Store = new CosmosDbEventStore(context, domain.EventTypes, new FixedServiceIdProvider(ServiceId), resolver);
        }

        public InMemoryCosmosClient Client { get; }
        public CosmosDbEventStoreOptions Options { get; }
        public CosmosDbEventStore Store { get; }
        public InMemoryCosmosContainer Events => Client.Container(Options.EventsContainerName);
        public InMemoryCosmosContainer Tags => Client.Container(Options.TagsContainerName);
        public Task<int> DurableCount() => Task.FromResult(Events.Items.Count);
    }

    private static CosmosDbEventStoreOptions NewOptions() =>
        new()
        {
            EventsContainerName = "events",
            TagsContainerName = "tags",
            WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward,
            TagWriteRetry = new CosmosTagWriteRetryOptions { MaxAttempts = 1, JitterRatio = 0 }
        };

    private Lineage NewLineage(InMemoryCosmosClient? client = null, CosmosDbEventStoreOptions? options = null) =>
        new(_domain, client ?? new InMemoryCosmosClient(), options ?? NewOptions());

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;
        public FixedServiceIdProvider(string serviceId) => _serviceId = serviceId;
        public string GetCurrentServiceId() => _serviceId;
    }

    private sealed class FailBeforeBatch : ICosmosTagWriteFaultInjector
    {
        private readonly int _batchIndex;
        public FailBeforeBatch(int batchIndex) => _batchIndex = batchIndex;
        public Task OnBeforeBatchAsync(int batchIndex, string partitionKey, IReadOnlyList<CosmosTag> rows)
        {
            if (batchIndex >= _batchIndex)
            {
                throw new InvalidOperationException($"Injected crash before tag batch {batchIndex}");
            }
            return Task.CompletedTask;
        }
    }

    private sealed record TestTag(string Value) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => Value.Split(':')[0];
        public string GetTag() => Value;
        public string GetTagContent() => Value.Split(':')[1];
    }

    // ── The shared uniform contract ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RealStoreHeadSeedsMonotonicGeneratorAbovePersistedMaximum()
    {
        var lineage = NewLineage();
        var persistedTicks = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var persisted = new Event(
                new ConditionalMarkerEvent("seed"),
                SortableUniqueId.Generate(new DateTime(persistedTicks, DateTimeKind.Utc), Guid.NewGuid()),
                nameof(ConditionalMarkerEvent),
                Guid.CreateVersion7(),
                new EventMetadata("seed", "seed", "test"),
                ["Marker:seed"])
            .ToSerializableEvent(_domain.EventTypes);
        Assert.True((await lineage.Store.WriteSerializableEventsAsync([persisted])).IsSuccess);

        var generator = new MonotonicSortableUniqueIdGenerator(
            new FixedTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await new SortableUniqueIdSeedCoordinator(generator).EnsureSeededAsync(ServiceId, lineage.Store);

        Assert.True(string.CompareOrdinal(generator.GenerateNew(), persisted.SortableUniqueIdValue) > 0);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public void Capability_ReportsSingleEventUniqueKey() =>
        ConditionalAppendScenarios.AssertCapability(NewLineage().Store);

    [Fact]
    public Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent()
    {
        var lineage = NewLineage();
        return ConditionalAppendScenarios.AssertFirstAppendWins_SameOpRetryIsIdempotent(
            lineage.Store, _domain, "cosmos-1", lineage.DurableCount);
    }

    [Fact]
    public Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause_NoSecondEvent()
    {
        var lineage = NewLineage();
        return ConditionalAppendScenarios.AssertDifferentOperationIsKeyReuseConflict_WithProviderCause(
            lineage.Store, _domain, "cosmos-2", lineage.DurableCount);
    }

    [Fact]
    public Task NWriters_SameOperation_ConcurrentWriters_OneAppended_RestAlreadyCommitted_OneDurableEvent()
    {
        // The in-memory container serializes concurrent creates under a lock exactly as a Cosmos partition does, so this
        // is a genuine concurrent-writer race, not sequential retries.
        var lineage = NewLineage();
        return ConditionalAppendScenarios.AssertNWritersConverge(lineage.Store, _domain, "cosmos-race", 8, lineage.DurableCount);
    }

    // ── In-doubt: a bare 409 with no committed winner is retryable in-doubt, never AlreadyCommitted ──

    [Fact]
    public async Task Bare409_WithoutACommittedWinner_IsTypedRetryableInDoubt()
    {
        var lineage = NewLineage();
        var store = (IConditionalEventStore)lineage.Store;
        lineage.Events.WriteFaults.Enqueue(CosmosFailures.Conflict());

        var result = await store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-indoubt", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(result.GetException());
        Assert.True(ex.IsRetryable);
        Assert.Equal(ConditionalAppendInDoubtReason.WinnerUnreadableAfterConflict, ex.Reason);
        Assert.Empty(lineage.Events.Items);

        // Recovery: the injected fault is one-shot, so a retry converges to a durable Appended.
        var recovered = await store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-indoubt", ConditionalAppendScenarios.Marker(_domain, "v")));
        Assert.True(recovered.IsSuccess);
        Assert.Equal(ConditionalAppendStatus.Appended, recovered.GetValue().Status);
        Assert.Single(lineage.Events.Items);
    }

    [Fact]
    public async Task PostCommitResponseLoss_Transport_FirstCallAmbiguous_Retry_ConvergesToAlreadyCommitted_WithTagVisibility()
    {
        // TRUE post-commit ambiguity: the event AND all tag rows are durable, then the response is lost via a transport
        // exception. First call ambiguous; a retry on a fresh store converges to AlreadyCommitted, with exact tag
        // visibility and no second event.
        var client = new InMemoryCosmosClient();
        var options = NewOptions();
        var first = NewLineage(client, options);
        var transport = new InvalidOperationException("connection reset after commit");
        first.Store.AfterConditionalCommitHook = () => throw transport;

        // Multi-tag event: the event AND all three tag rows are durable before the lost response.
        SerializableEvent ThreeTagMarker() =>
            new Event(new ConditionalMarkerEvent("v"), SortableUniqueId.GenerateNew(), nameof(ConditionalMarkerEvent),
                    Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), new List<string> { "T:a", "T:b", "T:c" })
                .ToSerializableEvent(_domain.EventTypes);

        // The event AND all three tag rows commit durably, then the response is lost. The store signals the post-commit
        // ambiguity marker; the shared orchestrator resolves it authoritatively ON THE SAME CALL — bounded read-back plus
        // the committed-state gate verifying all tag rows — to AlreadyCommitted with the exact stored-winner receipt.
        var deterministicId = ConditionalAppendIdentity.DeriveEventId(ServiceId, OperationFingerprint.NormalizeKey("cosmos-postcommit"));
        var resolved = await ((IConditionalEventStore)first.Store).AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-postcommit", ThreeTagMarker()));

        Assert.True(resolved.IsSuccess, resolved.IsSuccess ? "" : resolved.GetException().ToString());
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, resolved.GetValue().Status);
        Assert.Single(first.Events.Items);                   // exactly one event
        Assert.Equal(3, first.Tags.Items.Count);             // all three tag rows, no duplicates

        // A subsequent retry on a genuinely fresh store converges to the same AlreadyCommitted receipt, with every ordered
        // tag row present/visible and no duplicates.
        var winner = (await first.Store.ReadSerializableEventAsync(deterministicId)).GetValue();
        var second = NewLineage(client, options);
        var retry = await ((IConditionalEventStore)second.Store).AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-postcommit", ThreeTagMarker()));

        Assert.True(retry.IsSuccess, retry.IsSuccess ? "" : retry.GetException().ToString());
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, retry.GetValue().Status);
        ConditionalAppendScenarios.AssertReceiptMatchesStoredWinner(retry.GetValue(), ServiceId, "cosmos-postcommit", _domain, winner);
        Assert.Single(second.Events.Items);                  // no second event
        Assert.Equal(3, second.Tags.Items.Count);            // exactly the three tag rows, no duplicates
        foreach (var tag in new[] { "T:a", "T:b", "T:c" })
        {
            Assert.Single((await second.Store.ReadSerializableEventsByTagAsync(new TestTag(tag))).GetValue());
        }
    }

    [Fact]
    public async Task PostCommitResponseLoss_BoundedVerificationTimeout_OnStuckRead_IsTypedInDoubt_Promptly_NoLaterMutation()
    {
        // Production-path (not cooperative-delegate) proof that the bounded verification budget is enforced end to end:
        // the event+tags commit, the response is lost, and the authoritative winner read is STUCK. The read observes the
        // bounded budget token, so verification times out promptly to a typed AmbiguousAfterWrite — and nothing mutates
        // after the result returns.
        var lineage = NewLineage();
        // Override the verification budget on the store's coordinator (test seam, reached by reflection).
        var coordinator = typeof(CosmosDbEventStore)
            .GetField("_conditionalAppend", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(lineage.Store)!;
        coordinator.GetType().GetProperty("VerificationBudgetOverride",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(coordinator, TimeSpan.FromMilliseconds(200));

        lineage.Store.AfterConditionalCommitHook = () => throw new InvalidOperationException("response lost after commit");
        lineage.Events.ReadItemGate = ct => Task.Delay(Timeout.Infinite, ct); // winner read hangs until the budget cancels it

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await ((IConditionalEventStore)lineage.Store).AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-budget", ConditionalAppendScenarios.Marker(_domain, "v")));
        sw.Stop();

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(result.GetException());
        Assert.Equal(ConditionalAppendInDoubtReason.AmbiguousAfterWrite, ex.Reason);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"bounded verification should time out promptly, took {sw.Elapsed}");

        // No background mutation after the typed in-doubt returned: the counts are stable.
        lineage.Events.ReadItemGate = null;
        var creates = lineage.Events.Creates + lineage.Tags.Creates;
        var deletes = lineage.Events.Deletes + lineage.Tags.Deletes;
        await Task.Delay(150);
        Assert.Equal(creates, lineage.Events.Creates + lineage.Tags.Creates);
        Assert.Equal(deletes, lineage.Events.Deletes + lineage.Tags.Deletes);
    }

    // ── The crash-window/repair gate (Fix #1) ───────────────────────────────────────────────────────

    [Fact]
    public async Task CrashAfterEventCreate_BeforeTags_Retry_RepairsTags_ThenAlreadyCommitted_WithTagVisibility()
    {
        var client = new InMemoryCosmosClient();
        var options = NewOptions();
        var first = NewLineage(client, options);
        var store = (IConditionalEventStore)first.Store;

        // Crash the tag write: the event document lands, no tag row does.
        first.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        var crashed = await store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-crash", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.False(crashed.IsSuccess);          // the attempt did not reach committed state
        Assert.Single(first.Events.Items);         // event is durable
        Assert.Empty(first.Tags.Items);            // tag row never landed — the open window
        Assert.Empty((await first.Store.ReadSerializableEventsByTagAsync(new TestTag("Migration:once"))).GetValue());

        // Retry on a FRESH store instance (as a restarted host would). The event-only 409 must NOT short-circuit to
        // AlreadyCommitted: the gate repairs the missing tag row first, then returns the original winner's receipt.
        var second = NewLineage(client, options);
        var retryStore = (IConditionalEventStore)second.Store;
        var repaired = await retryStore.AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-crash", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.True(repaired.IsSuccess);
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, repaired.GetValue().Status);
        Assert.Single(second.Events.Items);        // still exactly one durable event
        Assert.Single(second.Tags.Items);          // the tag row is now present — window closed
        var byTag = await second.Store.ReadSerializableEventsByTagAsync(new TestTag("Migration:once"));
        Assert.Single(byTag.GetValue());           // exact tag-scoped visibility, not merely one event document
    }

    [Fact]
    public async Task Retry_WhenRepairStillFails_IsTypedInDoubt_NotFalseAlreadyCommitted()
    {
        var client = new InMemoryCosmosClient();
        var options = NewOptions();
        var first = NewLineage(client, options);

        first.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        await ((IConditionalEventStore)first.Store).AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-stuck", ConditionalAppendScenarios.Marker(_domain, "v")));
        Assert.Single(first.Events.Items);
        Assert.Empty(first.Tags.Items);

        // The retry's repair also fails (the tag store is still crashing): the committed state cannot be verified, so the
        // outcome is typed retryable in-doubt — never a false AlreadyCommitted while tag rows are still missing.
        var second = NewLineage(client, options);
        second.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        var result = await ((IConditionalEventStore)second.Store).AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-stuck", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(result.GetException());
        Assert.True(ex.IsRetryable);
        Assert.Equal(ConditionalAppendInDoubtReason.CommittedStateUnverified, ex.Reason);
        Assert.Empty(second.Tags.Items);
    }

    [Fact]
    public async Task Retry_WithMismatchedFirstRow_IsNonRetryableCorruption_NoOverwrite_NoLaterRepair_SecretSafe()
    {
        const string sentinel = "SENTINEL-SECRET-9F3-EVTTYPE";
        var client = new InMemoryCosmosClient();
        var options = NewOptions();
        var first = NewLineage(client, options);
        const string key = "cosmos-corrupt";

        // Event carries two tags; "Corrupt:a" is FIRST so its partition is repaired first (GroupBy preserves source
        // order). Crash the tag write: the event is durable, neither tag row landed.
        SerializableEvent TwoTagMarker() =>
            new Event(new ConditionalMarkerEvent("v"), SortableUniqueId.GenerateNew(), nameof(ConditionalMarkerEvent),
                    Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), new List<string> { "Corrupt:a", "Missing:b" })
                .ToSerializableEvent(_domain.EventTypes);

        first.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        await ((IConditionalEventStore)first.Store).AppendIfUniqueAsync(new ConditionalAppendRequest(key, TwoTagMarker()));
        Assert.Single(first.Events.Items);
        Assert.Empty(first.Tags.Items);

        // Seed ONE DISAGREEING row for the FIRST tag (wrong event type carrying a sentinel secret). The second tag's row
        // stays missing. Repair must not overwrite the corrupt row nor proceed to create the missing one.
        var deterministicId = ConditionalAppendIdentity.DeriveEventId(ServiceId, OperationFingerprint.NormalizeKey(key));
        var winner = (await first.Store.ReadSerializableEventAsync(deterministicId)).GetValue();
        first.Tags.Seed(CosmosTagIdentity.DeriveRow(ServiceId, "Corrupt:a", deterministicId, winner.SortableUniqueIdValue, sentinel));

        static string Snapshot(InMemoryCosmosContainer c) =>
            string.Join("\n", c.Items.Select(i => i.ToString(Newtonsoft.Json.Formatting.None)).OrderBy(s => s, StringComparer.Ordinal));
        var before = Snapshot(first.Tags);

        var second = NewLineage(client, options);
        var result = await ((IConditionalEventStore)second.Store).AppendIfUniqueAsync(new ConditionalAppendRequest(key, TwoTagMarker()));

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendCommittedStateCorruptionException>(result.GetException());
        Assert.False(ex.IsRetryable);
        Assert.Null(ex.InnerException);                                  // no unsafe provider exception chained
        ExceptionGraphSecretAssert.ContainsNoneOf(ex, sentinel, "Corrupt:a", "Missing:b"); // recursive graph secret-safe
        Assert.Single(second.Tags.Items);                               // the missing later row was NOT created
        Assert.Equal(before, Snapshot(second.Tags));                    // every existing byte unchanged (no overwrite)

        // Repeated retry stays corruption and still changes nothing.
        var again = await ((IConditionalEventStore)second.Store).AppendIfUniqueAsync(new ConditionalAppendRequest(key, TwoTagMarker()));
        Assert.IsType<ConditionalAppendCommittedStateCorruptionException>(again.GetException());
        Assert.Equal(before, Snapshot(second.Tags));
    }

    [Fact]
    public async Task DifferentOperation_UnderSameKey_IsKeyReuseConflict_EvenWhenPriorTagsMissing()
    {
        var client = new InMemoryCosmosClient();
        var options = NewOptions();
        var first = NewLineage(client, options);
        first.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        await ((IConditionalEventStore)first.Store).AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-diff", ConditionalAppendScenarios.Marker(_domain, "original")));
        Assert.Single(first.Events.Items);

        // A DIFFERENT operation reusing the key must still be a key-reuse conflict (fingerprint differs), regardless of
        // the prior partial commit — the repair gate is only reached on a fingerprint match.
        var second = NewLineage(client, options);
        var conflict = await ((IConditionalEventStore)second.Store).AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-diff", ConditionalAppendScenarios.Marker(_domain, "DIFFERENT")));

        Assert.False(conflict.IsSuccess);
        Assert.IsType<KeyReuseConflictException>(conflict.GetException());
    }
}
