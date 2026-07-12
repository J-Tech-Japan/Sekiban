using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Net;

namespace Sekiban.Dcb.Tests.Cosmos;

/// <summary>
///     An in-memory Cosmos container: enough of one to drive the real <c>CosmosDbEventStore</c>, the real
///     repair service, and the real sweep end to end, with no emulator and no timing.
///     It is a test double, not a Cosmos implementation. It recognizes the handful of query shapes those
///     types actually issue rather than interpreting SQL, and it models the two behaviors the crash-window
///     contracts hang on: a create conflicts on an existing (partition key, id), and a transactional batch is
///     all-or-nothing within one partition.
/// </summary>
public sealed class InMemoryCosmosContainer : NotSupportedCosmosContainer
{
    private readonly Dictionary<(string PartitionKey, string Id), JObject> _items = new();
    private readonly string _name;

    public InMemoryCosmosContainer(string name) => _name = name;

    /// <summary>Fails the next N writes with this, to model a store that is throttling or a host that dies.</summary>
    public Queue<Exception> WriteFaults { get; } = new();

    /// <summary>
    ///     Called with the running count before each document is written. Lets a test act on the Nth write —
    ///     cancel, crash, race a concurrent writer — at an exact point, instead of polling for one.
    /// </summary>
    public Action<int>? OnWrite { get; set; }

    /// <summary>Every document currently stored, newest last.</summary>
    public IReadOnlyList<JObject> Items => _items.Values.ToList();

    public int Creates { get; private set; }
    public int Deletes { get; private set; }
    public int Queries { get; private set; }

    public override string Id => _name;

    /// <summary>Puts a document in directly, bypassing the write path — for seeding a starting state.</summary>
    public void Seed(object document)
    {
        var item = JObject.FromObject(document);
        Stamp(item);
        _items[(Pk(item), Id_(item))] = item;
    }

    /// <summary>
    ///     Rewrites a stored document in place, as a concurrent writer would — which moves its ETag, so a
    ///     delete pinned to the old one is refused. This is how an ETag race is staged deterministically.
    /// </summary>
    public void MutateInPlace(string partitionKey, string id, Action<JObject> mutate)
    {
        var item = _items[(partitionKey, id)];
        mutate(item);
        Stamp(item);
    }

    public IEnumerable<JObject> ItemsIn(string partitionKey) =>
        _items.Where(entry => entry.Key.PartitionKey == partitionKey).Select(entry => entry.Value);

    /// <summary>Gives the document a fresh ETag, as Cosmos does on every write.</summary>
    private void Stamp(JObject item) => item["_etag"] = $"etag-{++_etagSequence}";

    private int _etagSequence;

    private static string Pk(JObject item) => item["pk"]?.Value<string>() ?? string.Empty;
    private static string Id_(JObject item) => item["id"]?.Value<string>() ?? string.Empty;
    private static string? ETagOf(JObject item) => item["_etag"]?.Value<string>();

    private void ThrowIfFaulted()
    {
        if (WriteFaults.Count > 0)
        {
            throw WriteFaults.Dequeue();
        }
    }

    public override Task<ItemResponse<T>> CreateItemAsync<T>(
        T item,
        PartitionKey? partitionKey = null,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfFaulted();
        OnWrite?.Invoke(Creates);
        Creates++;

        var document = JObject.FromObject(item!);
        var key = (Pk(document), Id_(document));

        if (_items.ContainsKey(key))
        {
            throw CosmosFailures.Conflict();
        }

        Stamp(document);
        _items[key] = document;
        return Task.FromResult<ItemResponse<T>>(new FakeItemResponse<T>(item, HttpStatusCode.Created));
    }

    public override Task<ItemResponse<T>> ReadItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        var match = _items
            .Where(entry => entry.Key.Id == id)
            .Select(entry => entry.Value)
            .FirstOrDefault();

        if (match == null)
        {
            throw CosmosFailures.NotFound();
        }

        return Task.FromResult<ItemResponse<T>>(
            new FakeItemResponse<T>(match.ToObject<T>()!, HttpStatusCode.OK));
    }

    public override Task<ItemResponse<T>> DeleteItemAsync<T>(
        string id,
        PartitionKey partitionKey,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
    {
        Deletes++;

        var key = _items.Keys.FirstOrDefault(entry => entry.Id == id);
        if (key == default)
        {
            throw CosmosFailures.NotFound();
        }

        // An ETag-guarded delete names the version it was planned against. If the row has moved since,
        // Cosmos refuses it — and so does this double, or the guard would be untested.
        var ifMatch = requestOptions?.IfMatchEtag;
        if (!string.IsNullOrEmpty(ifMatch) &&
            !string.Equals(ifMatch, ETagOf(_items[key]), StringComparison.Ordinal))
        {
            throw CosmosFailures.PreconditionFailed();
        }

        _items.Remove(key);
        return Task.FromResult<ItemResponse<T>>(new FakeItemResponse<T>(default!, HttpStatusCode.NoContent));
    }

    public override TransactionalBatch CreateTransactionalBatch(PartitionKey partitionKey) =>
        new InMemoryTransactionalBatch(this);

    /// <summary>
    ///     All-or-nothing within one partition, exactly as Cosmos behaves: if any create conflicts, nothing
    ///     is written. The crash-window contracts depend on this, so the double has to get it right.
    /// </summary>
    internal TransactionalBatchResponse ExecuteBatch(IReadOnlyList<JObject> documents)
    {
        ThrowIfFaulted();

        var conflicted = documents.Any(document => _items.ContainsKey((Pk(document), Id_(document))));
        if (conflicted)
        {
            return new FakeBatchResponse(
                HttpStatusCode.Conflict,
                documents.Select(_ => HttpStatusCode.Conflict).ToList());
        }

        foreach (var document in documents)
        {
            OnWrite?.Invoke(Creates);
            Creates++;
            Stamp(document);
            _items[(Pk(document), Id_(document))] = document;
        }

        return new FakeBatchResponse(
            HttpStatusCode.OK,
            documents.Select(_ => HttpStatusCode.Created).ToList());
    }

    public override FeedIterator<T> GetItemQueryIterator<T>(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null)
    {
        Queries++;

        var text = queryDefinition.QueryText;
        var parameters = queryDefinition
            .GetQueryParameters()
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value, StringComparer.Ordinal);

        var rows = Execute(text, parameters);

        // A query with an item cap pages; the store and the repair both follow continuation tokens.
        var pageSize = requestOptions?.MaxItemCount is > 0 ? requestOptions.MaxItemCount!.Value : rows.Count;
        var offset = continuationToken == null ? 0 : int.Parse(continuationToken, null);
        var page = rows.Skip(offset).Take(Math.Max(1, pageSize)).ToList();
        var consumed = offset + page.Count;
        var next = consumed < rows.Count ? consumed.ToString(null as IFormatProvider) : null;

        var typed = page.Select(Materialize<T>).ToList();
        return new FakeFeedIterator<T>(typed, next);
    }

    private static T Materialize<T>(JObject row) =>
        typeof(T) == typeof(long)
            ? (T)(object)row["__count"]!.Value<long>()
            : typeof(T) == typeof(string)
                ? (T)(object)row["__value"]!.Value<string>()!
                : typeof(T) == typeof(object)
                    ? (T)(object)row
                    : row.ToObject<T>()!;

    /// <summary>
    ///     Recognizes the query shapes the production code issues. Deliberately narrow: if a new query shape
    ///     appears, this throws rather than quietly returning nothing and turning a real bug into a green test.
    /// </summary>
    private List<JObject> Execute(string text, IReadOnlyDictionary<string, object> parameters)
    {
        var rows = _items.Values.AsEnumerable();

        // --- tags container -------------------------------------------------------------------------
        if (text.Contains("c.pk = @pk", StringComparison.Ordinal))
        {
            var pk = (string)parameters["@pk"];
            rows = rows.Where(row => Pk(row) == pk);

            // Repair's candidate prefilter: every Guid rendering, compared case-insensitively.
            if (text.Contains("STRINGEQUALS(c.eventId", StringComparison.Ordinal))
            {
                var renderings = parameters
                    .Where(parameter => parameter.Key.StartsWith("@eventId", StringComparison.Ordinal))
                    .Select(parameter => (string)parameter.Value)
                    .ToList();

                rows = rows.Where(row => renderings.Any(rendering =>
                    string.Equals(row["eventId"]?.Value<string>(), rendering, StringComparison.OrdinalIgnoreCase)));
            }

            if (parameters.TryGetValue("@since", out var since))
            {
                rows = rows.Where(row =>
                    string.CompareOrdinal(row["sortableUniqueId"]!.Value<string>(), (string)since) > 0);
            }

            if (text.Contains("COUNT(1)", StringComparison.Ordinal))
            {
                return Count(rows);
            }

            rows = text.Contains("DESC", StringComparison.Ordinal)
                ? rows.OrderByDescending(row => row["sortableUniqueId"]!.Value<string>(), StringComparer.Ordinal)
                : rows.OrderBy(row => row["sortableUniqueId"]!.Value<string>(), StringComparer.Ordinal);

            return rows.ToList();
        }

        // --- events container -----------------------------------------------------------------------
        if (text.Contains("c.serviceId = @serviceId", StringComparison.Ordinal))
        {
            var serviceId = (string)parameters["@serviceId"];
            rows = rows.Where(row => row["serviceId"]?.Value<string>() == serviceId);

            if (parameters.TryGetValue("@since", out var since))
            {
                rows = rows.Where(row =>
                    string.CompareOrdinal(row["sortableUniqueId"]!.Value<string>(), (string)since) > 0);
            }

            if (parameters.TryGetValue("@from", out var from))
            {
                rows = rows.Where(row =>
                    string.CompareOrdinal(row["sortableUniqueId"]!.Value<string>(), (string)from) > 0);
            }

            if (parameters.TryGetValue("@to", out var to))
            {
                rows = rows.Where(row =>
                    string.CompareOrdinal(row["sortableUniqueId"]!.Value<string>(), (string)to) <= 0);
            }

            if (text.Contains("COUNT(1)", StringComparison.Ordinal))
            {
                return Count(rows);
            }

            rows = text.Contains("DESC", StringComparison.Ordinal)
                ? rows.OrderByDescending(row => row["sortableUniqueId"]!.Value<string>(), StringComparer.Ordinal)
                : rows.OrderBy(row => row["sortableUniqueId"]!.Value<string>(), StringComparer.Ordinal);

            if (text.Contains("TOP 1 VALUE c.sortableUniqueId", StringComparison.Ordinal))
            {
                return rows
                    .Take(1)
                    .Select(row => new JObject { ["__value"] = row["sortableUniqueId"] })
                    .ToList();
            }

            return rows.ToList();
        }

        throw new NotSupportedException($"The in-memory container does not recognize this query: {text}");
    }

    private static List<JObject> Count(IEnumerable<JObject> rows) =>
        new() { new JObject { ["__count"] = rows.Count() } };
}

/// <summary>Cosmos failures the double needs to raise, shaped the way the production code matches on them.</summary>
public static class CosmosFailures
{
    public static CosmosException Conflict() =>
        new("conflict", HttpStatusCode.Conflict, 409, "activity", 1.0);

    public static CosmosException NotFound() =>
        new("not found", HttpStatusCode.NotFound, 404, "activity", 1.0);

    /// <summary>412: the row moved since the caller last saw it. The delete did not happen.</summary>
    public static CosmosException PreconditionFailed() =>
        new("etag mismatch", HttpStatusCode.PreconditionFailed, 412, "activity", 1.0);

    /// <summary>A 429 carrying the server's Retry-After, which the production code must honor in full.</summary>
    public static CosmosException Throttled(TimeSpan retryAfter) => new ThrottledException(retryAfter);

    private sealed class ThrottledException : CosmosException
    {
        private readonly TimeSpan _retryAfter;

        public ThrottledException(TimeSpan retryAfter)
            : base("throttled", HttpStatusCode.TooManyRequests, 429, "activity", 1.0) =>
            _retryAfter = retryAfter;

        public override TimeSpan? RetryAfter => _retryAfter;
    }
}

internal sealed class InMemoryTransactionalBatch : TransactionalBatch
{
    private readonly InMemoryCosmosContainer _container;
    private readonly List<JObject> _documents = new();

    public InMemoryTransactionalBatch(InMemoryCosmosContainer container) => _container = container;

    public override TransactionalBatch CreateItem<T>(T item, TransactionalBatchItemRequestOptions? requestOptions = null)
    {
        _documents.Add(JObject.FromObject(item!));
        return this;
    }

    public override Task<TransactionalBatchResponse> ExecuteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_container.ExecuteBatch(_documents));

    public override Task<TransactionalBatchResponse> ExecuteAsync(
        TransactionalBatchRequestOptions requestOptions,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(cancellationToken);

    public override TransactionalBatch CreateItemStream(
        Stream streamPayload,
        TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

    public override TransactionalBatch DeleteItem(string id, TransactionalBatchItemRequestOptions? requestOptions = null) =>
        throw new NotSupportedException("The write path never deletes through a batch.");

    public override TransactionalBatch PatchItem(
        string id,
        IReadOnlyList<PatchOperation> patchOperations,
        TransactionalBatchPatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

    public override TransactionalBatch ReadItem(string id, TransactionalBatchItemRequestOptions? requestOptions = null) =>
        throw new NotSupportedException();

    public override TransactionalBatch ReplaceItem<T>(
        string id,
        T item,
        TransactionalBatchItemRequestOptions? requestOptions = null) =>
        throw new NotSupportedException("The write path never replaces through a batch.");

    public override TransactionalBatch ReplaceItemStream(
        string id,
        Stream streamPayload,
        TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();

    public override TransactionalBatch UpsertItem<T>(T item, TransactionalBatchItemRequestOptions? requestOptions = null) =>
        throw new NotSupportedException("The write path never upserts.");

    public override TransactionalBatch UpsertItemStream(
        Stream streamPayload,
        TransactionalBatchItemRequestOptions? requestOptions = null) => throw new NotSupportedException();
}

internal sealed class FakeBatchResponse : TransactionalBatchResponse
{
    private readonly IReadOnlyList<HttpStatusCode> _operations;

    public FakeBatchResponse(HttpStatusCode statusCode, IReadOnlyList<HttpStatusCode> operations)
    {
        _statusCode = statusCode;
        _operations = operations;
    }

    private readonly HttpStatusCode _statusCode;
    public override HttpStatusCode StatusCode => _statusCode;
    public override bool IsSuccessStatusCode => (int)_statusCode is >= 200 and <= 299;
    public override int Count => _operations.Count;
    public override string ActivityId => "activity";
    public override double RequestCharge => _operations.Count;
    public override string? ErrorMessage => IsSuccessStatusCode ? null : "batch failed";
    private readonly Headers _headers = new();
    public override Headers Headers => _headers;
    public override CosmosDiagnostics Diagnostics => null!;
    public override TimeSpan? RetryAfter => null;

    public override TransactionalBatchOperationResult this[int index] => new FakeBatchOperationResult(_operations[index]);

    public override TransactionalBatchOperationResult<T> GetOperationResultAtIndex<T>(int index) =>
        throw new NotSupportedException();

    public override IEnumerator<TransactionalBatchOperationResult> GetEnumerator() =>
        _operations.Select(status => (TransactionalBatchOperationResult)new FakeBatchOperationResult(status))
            .GetEnumerator();
}

internal sealed class FakeBatchOperationResult : TransactionalBatchOperationResult
{
    public FakeBatchOperationResult(HttpStatusCode statusCode) => StatusCode = statusCode;

    public override HttpStatusCode StatusCode { get; }
    public override bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
    public override string? ETag => null;
    public override TimeSpan RetryAfter => TimeSpan.Zero;
}

internal sealed class FakeItemResponse<T> : ItemResponse<T>
{
    public FakeItemResponse(T resource, HttpStatusCode statusCode)
    {
        Resource = resource;
        StatusCode = statusCode;
    }

    public override T Resource { get; }
    public override HttpStatusCode StatusCode { get; }
    public override double RequestCharge => 1.0;
    public override string ActivityId => "activity";
    public override string? ETag => null;
    public override Headers Headers { get; } = new();
    public override CosmosDiagnostics Diagnostics => null!;
}

internal sealed class FakeFeedIterator<T> : FeedIterator<T>
{
    private readonly string? _continuationToken;
    private readonly IReadOnlyList<T> _rows;
    private bool _served;

    public FakeFeedIterator(IReadOnlyList<T> rows, string? continuationToken)
    {
        _rows = rows;
        _continuationToken = continuationToken;
    }

    public override bool HasMoreResults => !_served;

    public override Task<FeedResponse<T>> ReadNextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _served = true;
        return Task.FromResult<FeedResponse<T>>(new FakeFeedResponse<T>(_rows, _continuationToken));
    }
}

internal sealed class FakeFeedResponse<T> : FeedResponse<T>
{
    private readonly IReadOnlyList<T> _rows;

    public FakeFeedResponse(IReadOnlyList<T> rows, string? continuationToken)
    {
        _rows = rows;
        _continuation = continuationToken;
    }

    private readonly string? _continuation;
    public override string? ContinuationToken => _continuation;
    public override int Count => _rows.Count;
    public override Headers Headers { get; } = new();
    public override IEnumerable<T> Resource => _rows;
    public override double RequestCharge => _rows.Count;
    public override HttpStatusCode StatusCode => HttpStatusCode.OK;
    public override CosmosDiagnostics Diagnostics => null!;
    public override string IndexMetrics => string.Empty;

    public override IEnumerator<T> GetEnumerator() => _rows.GetEnumerator();
}
