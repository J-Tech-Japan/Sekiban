using System.Reflection;
using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage.Checkpoints;
using Xunit;
namespace Sekiban.Dcb.Tests.Checkpoints;

/// <summary>
///     SEK-G20 DynamoDB native version-condition CAS driven through the real DynamoMultiProjectionStateStore over a
///     round-trip DispatchProxy fake of IAmazonDynamoDB that evaluates the production ConditionExpression. Same state
///     machine + exactly-one-winner race as the reference; the (generation, revision, lifecycle) condition is the exact
///     token, and the required source lifecycle is hardcoded per op so a tombstone cannot be resurrected.
/// </summary>
public class DynamoDbCheckpointGenerationCasTests
{
    private const string Projector = "g20-p";
    private const string Version = "1.0.0";

    private static DynamoMultiProjectionStateStore NewStore(IAmazonDynamoDB client)
    {
        var options = new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            ProjectionStatesTableName = "states",
            UseConsistentReads = true
        };
        var context = new DynamoDbContext(client, Options.Create(options));
        return new DynamoMultiProjectionStateStore(context, new FixedServiceIdProvider("svc"));
    }

    private static MultiProjectionStateWriteRequest Req(long ep) => new(
        Projector, Version, "T", "s", ep, false, null, null, 1, 1, "w",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "test", "h");

    private static Stream Payload(string s) => new MemoryStream(Encoding.UTF8.GetBytes(s));
    private static async Task<CheckpointSlot> ReadAsync(IGenerationAwareCheckpointStore s) =>
        (await s.ReadCheckpointSlotAsync(Projector, Version)).GetValue();

    private static IAmazonDynamoDB NewClient() => DispatchProxy.Create<IAmazonDynamoDB, FakeDynamoDb>();

    [Fact]
    public void Capability_advertised() =>
        Assert.True(CheckpointCapabilityResolver.SupportsGenerationCas(NewStore(NewClient())));

    [Fact]
    public async Task FullLifecycle_WithVersionConditionCas()
    {
        var store = NewStore(NewClient());
        Assert.False((await ReadAsync(store)).Exists);

        var create = await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, create.Status);
        var active = await ReadAsync(store);
        Assert.True(active.IsActive);
        Assert.Equal(0, active.Generation);

        var persist = await store.ConditionalUpsertAsync(Req(2), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, persist.Status);
        var current = await ReadAsync(store);
        Assert.NotEqual(active.Revision, current.Revision);

        // Stale token rejected (exact token, not generation-only).
        var stale = await store.ConditionalUpsertAsync(Req(3), Payload("c"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, stale.Status);

        var inv = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(current));
        Assert.Equal(CheckpointCasStatus.Committed, inv.Status);
        var tomb = await ReadAsync(store);
        Assert.True(tomb.IsTombstoned);
        Assert.Equal(current.Generation + 1, tomb.Generation);

        // A normal persist cannot resurrect a tombstone (condition hardcodes the Active source lifecycle).
        var blocked = await store.ConditionalUpsertAsync(Req(4), Payload("d"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.ConditionRejected, blocked.Status);

        var commit = await store.CommitRebuiltAsync(Req(10), Payload("rebuilt"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, commit.Status);
        var final = await ReadAsync(store);
        Assert.True(final.IsActive);
        Assert.Equal(tomb.Generation, final.Generation);
    }

    [Fact]
    public async Task ConcurrentInvalidators_SameToken_ExactlyOneWinner()
    {
        var client = NewClient();
        var store = NewStore(client);
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);
        var expectation = CheckpointExpectation.FromSlot(active);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            NewStore(client).InvalidateWithTombstoneAsync(Projector, Version, expectation)));

        Assert.Equal(1, outcomes.Count(o => o.Status == CheckpointCasStatus.Committed));
        Assert.Equal(9, outcomes.Count(o => o.Status == CheckpointCasStatus.ConditionRejected));
        Assert.True((await ReadAsync(store)).IsTombstoned);
    }

    [Fact]
    public async Task PostCommitResponseLoss_ResolvesCommittedOrInDoubt_ViaBoundedReread()
    {
        var client = NewClient();
        var fake = (FakeDynamoDb)client;
        var store = NewStore(client);
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);

        // The PutItem COMMITS but its response is lost -> the store's bounded re-read confirms our own commit -> Committed.
        fake.PostWriteFaults.Enqueue(new IOException("injected: lost response after a committed write"));
        var committed = await store.ConditionalUpsertAsync(Req(2), Payload("b"), CheckpointExpectation.FromSlot(active), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, committed.Status);

        // The PutItem FAILS before committing -> the re-read cannot confirm our payload -> typed InDoubt with a cause.
        var current = await ReadAsync(store);
        fake.PreWriteFaults.Enqueue(new IOException("injected: lost response, write did not commit"));
        var indoubt = await store.ConditionalUpsertAsync(Req(3), Payload("c"), CheckpointExpectation.FromSlot(current), 1_000_000);
        Assert.Equal(CheckpointCasStatus.InDoubt, indoubt.Status);
        Assert.NotNull(indoubt.Cause);
    }

    [Fact]
    public async Task Invalidate_And_RebuiltCommit_PostAndPreAmbiguity_ResolveViaBoundedReread()
    {
        // SEK-G20 phase matrix for the OTHER two write ops (invalidate via UpdateItem, rebuilt via PutItem) through the real
        // Dynamo store over the round-trip fake: a post-write loss (the write committed) resolves Committed via the bounded
        // re-read; a pre-write loss (nothing committed) resolves typed InDoubt with the row unchanged.
        var client = NewClient();
        var fake = (FakeDynamoDb)client;
        var store = NewStore(client);
        await store.ConditionalUpsertAsync(Req(1), Payload("a"), CheckpointExpectation.Absent, 1_000_000);
        var active = await ReadAsync(store);

        // Invalidate, pre-write loss: nothing commits -> the row stays Active -> InDoubt.
        fake.PreWriteFaults.Enqueue(new IOException("injected: lost response, tombstone did not commit"));
        var invPre = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(active));
        Assert.Equal(CheckpointCasStatus.InDoubt, invPre.Status);
        Assert.True((await ReadAsync(store)).IsActive);

        // Invalidate, post-write loss: the tombstone (g+1) is durably written but the response is lost -> Committed.
        fake.PostWriteFaults.Enqueue(new IOException("injected: lost response after a committed tombstone"));
        var invPost = await store.InvalidateWithTombstoneAsync(Projector, Version, CheckpointExpectation.FromSlot(active));
        Assert.Equal(CheckpointCasStatus.Committed, invPost.Status);
        var tomb = await ReadAsync(store);
        Assert.True(tomb.IsTombstoned);
        Assert.Equal(active.Generation + 1, tomb.Generation);

        // Rebuilt commit, pre-write loss: nothing commits -> the row stays Tombstoned -> InDoubt.
        fake.PreWriteFaults.Enqueue(new IOException("injected: lost response, rebuilt did not commit"));
        var rebPre = await store.CommitRebuiltAsync(Req(9), Payload("R"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.InDoubt, rebPre.Status);
        Assert.True((await ReadAsync(store)).IsTombstoned);

        // Rebuilt commit, post-write loss: the rebuilt Active(g+1) is durably written but the response is lost -> Committed.
        fake.PostWriteFaults.Enqueue(new IOException("injected: lost response after a committed rebuild"));
        var rebPost = await store.CommitRebuiltAsync(Req(9), Payload("R"), CheckpointExpectation.FromSlot(tomb), 1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, rebPost.Status);
        var final = await ReadAsync(store);
        Assert.True(final.IsActive);
        Assert.Equal(tomb.Generation, final.Generation);
        Assert.Equal(9, final.Record!.EventsProcessed);
    }

    [Fact]
    public async Task PreG20Item_MissingControlAttributes_ReadsAsGeneration0_Active()
    {
        var store = NewStore(NewClient());
        Assert.True((await store.UpsertFromStreamAsync(Req(7), Payload("legacy"), 1_000_000)).GetValue());
        var slot = await ReadAsync(store);
        Assert.True(slot.IsActive);
        Assert.Equal(0, slot.Generation);
    }

    /// <summary>
    ///     Round-trip fake DynamoDB: stores exactly the item maps the writer emits and evaluates the production
    ///     ConditionExpression (attribute_not_exists(pk) and exact generation/revision/lifecycle equality), throwing
    ///     ConditionalCheckFailedException on mismatch — so the version-condition CAS is proven, not assumed.
    /// </summary>
    public class FakeDynamoDb : DispatchProxy
    {
        private readonly Dictionary<(string Pk, string Sk), Dictionary<string, AttributeValue>> _items = new();

        // SEK-G20: inject a lost response BEFORE the write commits (PreWriteFaults) or AFTER it commits (PostWriteFaults).
        public readonly Queue<Exception> PreWriteFaults = new();
        public readonly Queue<Exception> PostWriteFaults = new();
        private readonly object _gate = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name ?? string.Empty;
            var arg0 = args is { Length: > 0 } ? args[0] : null;
            switch (arg0)
            {
                case GetItemRequest get when name == nameof(IAmazonDynamoDB.GetItemAsync):
                    return Task.FromResult(GetItem(get));
                case PutItemRequest put when name == nameof(IAmazonDynamoDB.PutItemAsync):
                    return Task.FromResult(PutItem(put));
                case UpdateItemRequest upd when name == nameof(IAmazonDynamoDB.UpdateItemAsync):
                    return Task.FromResult(UpdateItem(upd));
            }
            if (name == "Dispose") return null;
            if (name.StartsWith("get_", StringComparison.Ordinal)) return null;
            throw new NotSupportedException($"FakeDynamoDb does not support {name}");
        }

        private static (string, string) KeyOf(Dictionary<string, AttributeValue> item) => (item["pk"].S, item["sk"].S);

        private GetItemResponse GetItem(GetItemRequest req)
        {
            lock (_gate)
            {
                var key = (req.Key["pk"].S, req.Key["sk"].S);
                return _items.TryGetValue(key, out var item)
                    ? new GetItemResponse { Item = new Dictionary<string, AttributeValue>(item) }
                    : new GetItemResponse { Item = new Dictionary<string, AttributeValue>() };
            }
        }

        private PutItemResponse PutItem(PutItemRequest req)
        {
            lock (_gate)
            {
                var key = KeyOf(req.Item);
                _items.TryGetValue(key, out var existing);
                if (!EvaluateCondition(req.ConditionExpression, existing, req.ExpressionAttributeNames, req.ExpressionAttributeValues))
                {
                    throw new ConditionalCheckFailedException("The conditional request failed");
                }
                if (PreWriteFaults.Count > 0) throw PreWriteFaults.Dequeue();   // lost response, write did NOT commit
                _items[key] = new Dictionary<string, AttributeValue>(req.Item);
                if (PostWriteFaults.Count > 0) throw PostWriteFaults.Dequeue(); // lost response AFTER a committed write
                return new PutItemResponse();
            }
        }

        private UpdateItemResponse UpdateItem(UpdateItemRequest req)
        {
            lock (_gate)
            {
                var key = (req.Key["pk"].S, req.Key["sk"].S);
                _items.TryGetValue(key, out var existing);
                if (existing is null)
                {
                    throw new ConditionalCheckFailedException("The conditional request failed");
                }
                if (!EvaluateCondition(req.ConditionExpression, existing, req.ExpressionAttributeNames, req.ExpressionAttributeValues))
                {
                    throw new ConditionalCheckFailedException("The conditional request failed");
                }
                if (PreWriteFaults.Count > 0) throw PreWriteFaults.Dequeue();   // lost response, update did NOT commit
                // Apply a "SET #a = :v, #b = :w" update expression.
                var body = req.UpdateExpression.Trim();
                if (body.StartsWith("SET ", StringComparison.OrdinalIgnoreCase))
                {
                    body = body.Substring(4);
                }
                foreach (var assignment in body.Split(','))
                {
                    var parts = assignment.Split('=', 2);
                    var attr = ResolveName(parts[0].Trim(), req.ExpressionAttributeNames);
                    var value = req.ExpressionAttributeValues[parts[1].Trim()];
                    existing[attr] = value;
                }
                if (PostWriteFaults.Count > 0) throw PostWriteFaults.Dequeue(); // lost response AFTER a committed update
                return new UpdateItemResponse();
            }
        }

        private static string ResolveName(string token, Dictionary<string, string>? names) =>
            token.StartsWith('#') && names != null && names.TryGetValue(token, out var real) ? real : token;

        private static bool EvaluateCondition(
            string? condition,
            Dictionary<string, AttributeValue>? existing,
            Dictionary<string, string>? names,
            Dictionary<string, AttributeValue>? values)
        {
            if (string.IsNullOrWhiteSpace(condition))
            {
                return true;
            }
            if (condition.Contains("attribute_not_exists(pk)", StringComparison.Ordinal))
            {
                return existing is null;
            }
            if (existing is null)
            {
                return false;
            }
            // "#g = :eg AND #r = :er AND #l = :el"
            foreach (var clause in condition.Split(new[] { " AND " }, StringSplitOptions.None))
            {
                var parts = clause.Split('=', 2);
                var attr = ResolveName(parts[0].Trim(), names);
                var expected = values![parts[1].Trim()];
                if (!existing.TryGetValue(attr, out var actual) ||
                    !string.Equals(actual.N, expected.N, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
