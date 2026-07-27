using System.Reflection;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Xunit;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G18 (#1086) provider boundary matrix — DynamoDB. Drives the REAL <see cref="DynamoDbEventStore" /> read
///     construction (GSI query with KeyConditionExpression <c>gsi1pk = :pk AND sortableUniqueId &gt; :since</c>) through a
///     round-trip <see cref="DispatchProxy" /> fake of <see cref="IAmazonDynamoDB" />: the fake stores exactly the items
///     the production writer emits and, on query, filters them by honoring the operator PARSED FROM the production
///     KeyConditionExpression. Writing P1&lt;P2&lt;P3 and reading since=P2 must yield ONLY P3 — P1 (&lt; P2) and the
///     at-position P2 (== P2) both excluded. If production regressed <c>&gt; :since</c> to <c>&gt;= :since</c>, the fake
///     would return P2 and this fails (non-vacuous).
/// </summary>
public class DynamoDbExclusiveAfterPositionTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string P1 = SortableUniqueId.Generate(BaseTime, Guid.Empty);
    private static readonly string P2 = SortableUniqueId.Generate(BaseTime.AddSeconds(1), Guid.Empty);
    private static readonly string P3 = SortableUniqueId.Generate(BaseTime.AddSeconds(2), Guid.Empty);

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;
        public FixedServiceIdProvider(string serviceId) => _serviceId = serviceId;
        public string GetCurrentServiceId() => _serviceId;
    }

    private static Event Ev(string sortableId) => new(
        new StudentCreated(Guid.NewGuid(), "n", 5),
        sortableId,
        nameof(StudentCreated),
        Guid.NewGuid(),
        new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
        new List<string>());

    [Fact]
    public async Task ReadAllEventsAsync_AfterPosition_ExcludesTheEventAtThePosition()
    {
        var options = new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,   // no DescribeTable/CreateTable calls against the fake
            WriteShardCount = 1,
            EventsTableName = "events",
            TagsTableName = "tags"
        };
        var client = DispatchProxy.Create<IAmazonDynamoDB, FakeDynamoDb>();
        var context = new DynamoDbContext(client, Options.Create(options));
        var store = new DynamoDbEventStore(
            context, DomainType.GetDomainTypes().EventTypes, new FixedServiceIdProvider("svc"));

        Assert.True((await store.WriteEventsAsync(new[] { Ev(P1), Ev(P2), Ev(P3) })).IsSuccess);

        var read = (await store.ReadAllEventsAsync(new SortableUniqueId(P2))).GetValue().ToList();

        Assert.Single(read);                                  // P1 (< P2) and P2 (== P2) excluded
        Assert.Equal(P3, read[0].SortableUniqueIdValue);      // only the strictly-later event
    }

    /// <summary>
    ///     A round-trip fake DynamoDB: it stores exactly the item maps the production writer emits (via
    ///     TransactWriteItems / BatchWriteItem) and, on Query, filters them by the GSI partition key and by honoring the
    ///     comparison operator PARSED from the production KeyConditionExpression — so item shape is never hand-forged and
    ///     the exclusive-after-position semantics are the production ones.
    /// </summary>
    public class FakeDynamoDb : DispatchProxy
    {
        private readonly List<Dictionary<string, AttributeValue>> _items = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name ?? string.Empty;
            var arg0 = args is { Length: > 0 } ? args[0] : null;

            switch (arg0)
            {
                case TransactWriteItemsRequest transact when name == nameof(IAmazonDynamoDB.TransactWriteItemsAsync):
                    foreach (var item in transact.TransactItems)
                    {
                        if (item.Put is { } put)
                        {
                            _items.Add(put.Item);
                        }
                    }
                    return Task.FromResult(new TransactWriteItemsResponse());

                case BatchWriteItemRequest batch when name == nameof(IAmazonDynamoDB.BatchWriteItemAsync):
                    foreach (var writes in batch.RequestItems.Values)
                    {
                        foreach (var write in writes)
                        {
                            if (write.PutRequest is { } put)
                            {
                                _items.Add(put.Item);
                            }
                        }
                    }
                    return Task.FromResult(new BatchWriteItemResponse { UnprocessedItems = new() });

                case QueryRequest query when name == nameof(IAmazonDynamoDB.QueryAsync):
                    return Task.FromResult(ExecuteQuery(query));
            }

            if (name == "Dispose")
            {
                return null;
            }
            if (name.StartsWith("get_", StringComparison.Ordinal))
            {
                return null;
            }
            throw new NotSupportedException($"FakeDynamoDb does not support {name}");
        }

        private QueryResponse ExecuteQuery(QueryRequest query)
        {
            var pk = query.ExpressionAttributeValues[":pk"].S;
            IEnumerable<Dictionary<string, AttributeValue>> rows =
                _items.Where(item => item.TryGetValue("gsi1pk", out var g) && g.S == pk);

            if (query.ExpressionAttributeValues.TryGetValue(":since", out var since))
            {
                rows = rows.Where(item => SinceMatches(query.KeyConditionExpression, item["sortableUniqueId"].S, since.S));
            }

            var ordered = rows.OrderBy(item => item["sortableUniqueId"].S, StringComparer.Ordinal).ToList();
            return new QueryResponse
            {
                Items = ordered,
                Count = ordered.Count,
                LastEvaluatedKey = new Dictionary<string, AttributeValue>()   // empty => single page
            };
        }

        // Honor the ACTUAL operator emitted by the production KeyConditionExpression ('> :since' exclusive vs '>= :since'
        // inclusive), so a '>' -> '>=' regression is caught.
        private static bool SinceMatches(string keyConditionExpression, string sortableUniqueId, string since)
        {
            var cmp = string.CompareOrdinal(sortableUniqueId, since);
            return keyConditionExpression.Contains("sortableUniqueId >= :since", StringComparison.Ordinal)
                ? cmp >= 0
                : cmp > 0;
        }
    }
}
