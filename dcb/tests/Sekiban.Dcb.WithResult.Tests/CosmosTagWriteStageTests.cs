using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Tags;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Covers the Cosmos tag-write stage: deterministic row identity, idempotent re-execution,
///     convergence after a partial write, and detection of a corrupt tag index.
/// </summary>
public class CosmosTagWriteStageTests
{
    private const string ServiceId = "svc";

    /// <summary>
    ///     In-memory stand-in for the tags container. Batches are all-or-nothing, and a create against an
    ///     existing identity conflicts, exactly as Cosmos behaves.
    /// </summary>
    private sealed class FakeTagRowStore : ICosmosTagRowStore
    {
        private readonly Dictionary<(string PartitionKey, string Id), CosmosTag> _rows = new();

        public int BatchAttempts { get; private set; }
        public int RowCreateAttempts { get; private set; }
        public IReadOnlyCollection<CosmosTag> Rows => _rows.Values;

        public void Seed(CosmosTag row) => _rows[(row.Pk, row.Id)] = row;

        public Task<CosmosTagBatchOutcome> CreateBatchAsync(string partitionKey, IReadOnlyList<CosmosTag> rows, CancellationToken cancellationToken = default)
        {
            BatchAttempts++;

            if (rows.Any(row => _rows.ContainsKey((partitionKey, row.Id))))
            {
                return Task.FromResult(CosmosTagBatchOutcome.Conflict);
            }

            foreach (var row in rows)
            {
                _rows[(partitionKey, row.Id)] = row;
            }

            return Task.FromResult(CosmosTagBatchOutcome.Created);
        }

        public Task<bool> TryCreateRowAsync(string partitionKey, CosmosTag row, CancellationToken cancellationToken = default)
        {
            RowCreateAttempts++;

            if (_rows.ContainsKey((partitionKey, row.Id)))
            {
                return Task.FromResult(false);
            }

            _rows[(partitionKey, row.Id)] = row;
            return Task.FromResult(true);
        }

        public Task<CosmosTag?> TryReadRowAsync(string partitionKey, string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows.GetValueOrDefault((partitionKey, id)));
    }

    /// <summary>
    ///     Fails the stage before batch number <c>failBeforeBatchIndex</c> — deterministic, no timing.
    /// </summary>
    private sealed class FailBeforeBatchFaultInjector : ICosmosTagWriteFaultInjector
    {
        private readonly int _failBeforeBatchIndex;

        public FailBeforeBatchFaultInjector(int failBeforeBatchIndex) => _failBeforeBatchIndex = failBeforeBatchIndex;

        public Task OnBeforeBatchAsync(int batchIndex, string partitionKey, IReadOnlyList<CosmosTag> rows)
        {
            if (batchIndex >= _failBeforeBatchIndex)
            {
                throw new InvalidOperationException($"Injected tag-write failure at batch {batchIndex}");
            }

            return Task.CompletedTask;
        }
    }

    private static CosmosTagRowSource Source(string tag, Guid eventId, string sortableUniqueId) =>
        new(tag, eventId, sortableUniqueId, "TestEvent");

    private static string NewSortableUniqueId() => SortableUniqueId.GenerateNew();

    private static Task<List<Sekiban.Dcb.Tags.TagWriteResult>> WriteAsync(
        IReadOnlyList<CosmosTagRowSource> sources,
        ICosmosTagRowStore store,
        CosmosDbEventStoreOptions? options = null,
        ICosmosTagWriteFaultInjector? faultInjector = null) =>
        CosmosTagWriteStage.WriteAsync(
            sources,
            store,
            options ?? new CosmosDbEventStoreOptions(),
            ServiceId,
            faultInjector);

    [Fact]
    public void DeriveRow_Should_Be_Deterministic_For_The_Same_Event_And_Tag()
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        var first = CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", eventId, sortableUniqueId, "TestEvent");
        var second = CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", eventId, sortableUniqueId, "TestEvent");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Pk, second.Pk);
        Assert.True(CosmosTagIdentity.ContentEquals(first, second));

        Assert.Equal(eventId.ToString(), first.Id);
        Assert.Equal($"{ServiceId}|Student:1", first.Pk);
        Assert.Equal("Student", first.TagGroup);
        Assert.Equal(new SortableUniqueId(sortableUniqueId).GetDateTime(), first.CreatedAt);
    }

    [Fact]
    public void DeriveRow_Should_Distinguish_Different_Events_And_Tags()
    {
        var sortableUniqueId = NewSortableUniqueId();
        var eventA = Guid.NewGuid();
        var eventB = Guid.NewGuid();

        var rowA = CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", eventA, sortableUniqueId, "TestEvent");
        var rowB = CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", eventB, sortableUniqueId, "TestEvent");
        var rowC = CosmosTagIdentity.DeriveRow(ServiceId, "Student:2", eventA, sortableUniqueId, "TestEvent");

        Assert.NotEqual(rowA.Id, rowB.Id);
        Assert.Equal(rowA.Pk, rowB.Pk);
        Assert.Equal(rowA.Id, rowC.Id);
        Assert.NotEqual(rowA.Pk, rowC.Pk);
    }

    [Fact]
    public async Task Re_Executing_A_Completed_Write_Should_Not_Duplicate_Rows()
    {
        var store = new FakeTagRowStore();
        var sources = new[]
        {
            Source("Student:1", Guid.NewGuid(), NewSortableUniqueId()),
            Source("Student:2", Guid.NewGuid(), NewSortableUniqueId())
        };

        var first = await WriteAsync(sources, store);
        var second = await WriteAsync(sources, store);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(2, store.Rows.Count);
    }

    [Fact]
    public async Task Re_Execution_After_A_Partial_Write_Should_Converge()
    {
        var store = new FakeTagRowStore();
        var sources = new[]
        {
            Source("Student:1", Guid.NewGuid(), NewSortableUniqueId()),
            Source("Student:2", Guid.NewGuid(), NewSortableUniqueId()),
            Source("Student:3", Guid.NewGuid(), NewSortableUniqueId())
        };

        // Each tag is its own partition, so each is its own batch: fail once two have been written.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => WriteAsync(sources, store, faultInjector: new FailBeforeBatchFaultInjector(2)));

        Assert.Equal(2, store.Rows.Count);

        var replay = await WriteAsync(sources, store);

        Assert.Equal(3, replay.Count);
        Assert.Equal(3, store.Rows.Count);
        Assert.Equal(
            sources.Select(source => source.EventId.ToString()).OrderBy(id => id, StringComparer.Ordinal),
            store.Rows.Select(row => row.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Existing_Row_With_Different_Content_Should_Raise_Corruption_And_Not_Overwrite()
    {
        var store = new FakeTagRowStore();
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        var corrupt = CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", eventId, sortableUniqueId, "TestEvent");
        corrupt.EventType = "SomethingElse";
        store.Seed(corrupt);

        var exception = await Assert.ThrowsAsync<CosmosTagIndexCorruptionException>(
            () => WriteAsync(new[] { Source("Student:1", eventId, sortableUniqueId) }, store));

        Assert.Equal(ServiceId, exception.ServiceId);
        Assert.Equal("Student:1", exception.Tag);
        Assert.Equal($"{ServiceId}|Student:1", exception.PartitionKey);
        Assert.Equal(eventId.ToString(), exception.DocumentId);

        // The existing row is left exactly as it was — never silently overwritten.
        var stored = Assert.Single(store.Rows);
        Assert.Equal("SomethingElse", stored.EventType);
    }

    [Fact]
    public async Task Identical_Existing_Row_Should_Count_As_Success()
    {
        var store = new FakeTagRowStore();
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        store.Seed(CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", eventId, sortableUniqueId, "TestEvent"));

        var results = await WriteAsync(new[] { Source("Student:1", eventId, sortableUniqueId) }, store);

        Assert.Single(results);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task A_Tag_Repeated_Within_One_Event_Should_Produce_One_Row()
    {
        var store = new FakeTagRowStore();
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        var results = await WriteAsync(
            new[]
            {
                Source("Student:1", eventId, sortableUniqueId),
                Source("Student:1", eventId, sortableUniqueId)
            },
            store);

        Assert.Single(results);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task Rows_Beyond_MaxBatchOperations_Should_Be_Chunked_And_Stay_Idempotent()
    {
        var store = new FakeTagRowStore();
        var options = new CosmosDbEventStoreOptions { MaxBatchOperations = 2 };
        var sortableUniqueId = NewSortableUniqueId();

        // One partition (one tag), five rows -> three batches of at most two.
        var sources = Enumerable
            .Range(0, 5)
            .Select(_ => Source("Student:1", Guid.NewGuid(), sortableUniqueId))
            .ToList();

        var first = await WriteAsync(sources, store, options);
        Assert.Equal(5, first.Count);
        Assert.Equal(5, store.Rows.Count);
        Assert.Equal(3, store.BatchAttempts);

        var second = await WriteAsync(sources, store, options);
        Assert.Equal(5, second.Count);
        Assert.Equal(5, store.Rows.Count);
    }

    [Fact]
    public async Task Writes_Should_Stay_Idempotent_Without_TransactionalBatch()
    {
        var store = new FakeTagRowStore();
        var options = new CosmosDbEventStoreOptions { UseTransactionalBatchForTags = false };
        var sources = new[]
        {
            Source("Student:1", Guid.NewGuid(), NewSortableUniqueId()),
            Source("Student:2", Guid.NewGuid(), NewSortableUniqueId())
        };

        await WriteAsync(sources, store, options);
        await WriteAsync(sources, store, options);

        Assert.Equal(0, store.BatchAttempts);
        Assert.Equal(2, store.Rows.Count);
    }

    [Fact]
    public async Task A_Conflicting_Batch_Should_Settle_Row_By_Row()
    {
        var store = new FakeTagRowStore();
        var options = new CosmosDbEventStoreOptions { MaxBatchOperations = 10 };
        var sortableUniqueId = NewSortableUniqueId();
        var existing = Guid.NewGuid();
        var missing = Guid.NewGuid();

        // The state a crash between batches leaves behind: one row of the partition present, one absent.
        store.Seed(CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", existing, sortableUniqueId, "TestEvent"));

        var results = await WriteAsync(
            new[]
            {
                Source("Student:1", existing, sortableUniqueId),
                Source("Student:1", missing, sortableUniqueId)
            },
            store,
            options);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, store.Rows.Count);
        Assert.Equal(1, store.BatchAttempts);
        Assert.Equal(2, store.RowCreateAttempts);
    }
}
