using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Sekiban.Dcb.CosmosDb.Emulator.Tests;

[Collection(CosmosEmulatorCollection.Name)]
public sealed class EngineSemanticsTests(CosmosEmulatorFixture fixture, ITestOutputHelper output)
{
    internal static JObject Head(string pk, int position = 0) => new()
    {
        ["id"] = "$head", ["pk"] = pk, ["serviceId"] = pk.Split('|')[0],
        ["tag"] = pk.Split('|').Last(), ["documentType"] = "tagHead", ["position"] = Position(position)
    };
    private static JObject Row(string pk, int index, string? id = null) => new()
    {
        ["id"] = id ?? $"row-{index}", ["pk"] = pk, ["serviceId"] = "svc",
        ["tag"] = pk.Split('|').Last(), ["sortableUniqueId"] = Position(index)
    };
    private static string Position(int value) => $"{value:D30}";
    private static PatchOperation[] SetPosition(int value) => [PatchOperation.Set("/position", Position(value))];
    private static TransactionalBatchPatchItemRequestOptions Compare(int expected) =>
        new() { FilterPredicate = $"FROM c WHERE c.position = '{Position(expected)}'" };

    private static async Task<Container> CreateContainerAsync(TestDatabase db) =>
        (await db.Database.CreateContainerAsync("tags", "/pk")).Container;

    internal static async Task<List<T>> QueryAsync<T>(Container container, string sql, string? pk = null)
    {
        var query = new QueryDefinition(sql);
        if (pk != null) query.WithParameter("@pk", pk);
        using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: pk == null ? null :
            new QueryRequestOptions { PartitionKey = new PartitionKey(pk) });
        var items = new List<T>();
        while (iterator.HasMoreResults) items.AddRange(await iterator.ReadNextAsync());
        return items;
    }
    private static async Task<int> CountAsync(Container c, string pk) =>
        Assert.Single(await QueryAsync<int>(c, "SELECT VALUE COUNT(1) FROM c WHERE c.pk = @pk", pk));
    private static async Task<string> ReadPositionAsync(Container c, string pk) =>
        (await c.ReadItemAsync<JObject>("$head", new PartitionKey(pk))).Resource["position"]!.ToString();
    private static async Task AssertMissingAsync(Container c, string pk, string id)
    {
        var error = await Assert.ThrowsAsync<CosmosException>(() => c.ReadItemAsync<JObject>(id, new PartitionKey(pk)));
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
    }
    private void AssertStatuses(TransactionalBatchResponse response, HttpStatusCode status, params HttpStatusCode[] operations)
    {
        output.WriteLine($"Batch {(int)response.StatusCode}; operations [{string.Join(",", response.Select(r => (int)r.StatusCode))}]");
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(operations, response.Select(r => r.StatusCode));
    }

    [SkippableTheory]
    [InlineData("holds", HttpStatusCode.OK)]
    [InlineData("fails", HttpStatusCode.PreconditionFailed)]
    [InlineData("missing", HttpStatusCode.NotFound)]
    [InlineData("conflict", HttpStatusCode.Conflict)]
    public async Task ConditionalPatchBatchIsAtomic(string scenario, HttpStatusCode status)
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var c = await CreateContainerAsync(db);
        const string pk = "svc|Student:batch";
        if (scenario != "missing") await c.CreateItemAsync(Head(pk), new PartitionKey(pk));
        if (scenario == "conflict") await c.CreateItemAsync(Row(pk, 2), new PartitionKey(pk));
        var before = await CountAsync(c, pk);
        using var response = await c.CreateTransactionalBatch(new PartitionKey(pk))
            .PatchItem("$head", SetPosition(2), Compare(scenario == "fails" ? 99 : 0))
            .CreateItem(Row(pk, 1)).CreateItem(Row(pk, 2)).ExecuteAsync();
        var ops = scenario switch
        {
            "holds" => new[] { HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.Created },
            "conflict" => [HttpStatusCode.FailedDependency, HttpStatusCode.FailedDependency, HttpStatusCode.Conflict],
            _ => [status, HttpStatusCode.FailedDependency, HttpStatusCode.FailedDependency]
        };
        AssertStatuses(response, status, ops);
        Assert.Equal(before + (scenario == "holds" ? 2 : 0), await CountAsync(c, pk));
        if (scenario != "missing") Assert.Equal(Position(scenario == "holds" ? 2 : 0), await ReadPositionAsync(c, pk));
        if (scenario != "holds")
        {
            await AssertMissingAsync(c, pk, "row-1");
            if (scenario != "conflict") await AssertMissingAsync(c, pk, "row-2");
        }
    }

    [SkippableFact]
    public async Task ConcurrentCompareAndSetHasExactlyOneWinner()
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var c = await CreateContainerAsync(db);
        const string pk = "svc|Student:race";
        await c.CreateItemAsync(Head(pk), new PartitionKey(pk));
        var outcomes = await Task.WhenAll(Enumerable.Range(1, 12).Select(async i =>
        {
            using var result = await c.CreateTransactionalBatch(new PartitionKey(pk)).CreateItem(Row(pk, i))
                .PatchItem("$head", SetPosition(i), Compare(0)).ExecuteAsync();
            return (Index: i, Status: result.StatusCode);
        }));
        output.WriteLine(string.Join(", ", outcomes.Select(r => $"{r.Index}:{(int)r.Status}")));
        var winner = Assert.Single(outcomes.Where(r => r.Status == HttpStatusCode.OK));
        Assert.All(outcomes.Where(r => r.Index != winner.Index), r => Assert.Equal(HttpStatusCode.PreconditionFailed, r.Status));
        Assert.Equal(Position(winner.Index), await ReadPositionAsync(c, pk));
        Assert.Equal(2, await CountAsync(c, pk));
        Assert.Equal($"row-{winner.Index}", Assert.Single((await QueryAsync<JObject>(c,
            "SELECT * FROM c WHERE c.pk = @pk AND NOT IS_DEFINED(c.documentType)", pk)))["id"]!.ToString());
    }

    [SkippableFact]
    public async Task ReplaceWithFreshThenStaleEtagIsAtomic()
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var c = await CreateContainerAsync(db);
        const string pk = "svc|Student:etag";
        var original = await c.CreateItemAsync(Head(pk), new PartitionKey(pk));
        using (var fresh = await c.CreateTransactionalBatch(new PartitionKey(pk)).CreateItem(Row(pk, 1))
            .ReplaceItem("$head", Head(pk, 1), new TransactionalBatchItemRequestOptions { IfMatchEtag = original.ETag }).ExecuteAsync())
        {
            AssertStatuses(fresh, HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.OK);
            Assert.NotEqual(original.ETag, fresh[1].ETag);
            Assert.Equal(fresh[1].ETag, (await c.ReadItemAsync<JObject>("$head", new PartitionKey(pk))).ETag);
        }
        using var stale = await c.CreateTransactionalBatch(new PartitionKey(pk)).CreateItem(Row(pk, 2))
            .ReplaceItem("$head", Head(pk, 2), new TransactionalBatchItemRequestOptions { IfMatchEtag = original.ETag }).ExecuteAsync();
        AssertStatuses(stale, HttpStatusCode.PreconditionFailed, HttpStatusCode.FailedDependency, HttpStatusCode.PreconditionFailed);
        Assert.Equal(2, await CountAsync(c, pk));
        Assert.Equal(Position(1), await ReadPositionAsync(c, pk));
        await AssertMissingAsync(c, pk, "row-2");
    }

    [SkippableFact]
    public async Task ReplaceMissingHeadRollsBack()
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var c = await CreateContainerAsync(db);
        const string pk = "svc|Student:missing";
        using var result = await c.CreateTransactionalBatch(new PartitionKey(pk)).CreateItem(Row(pk, 1))
            .ReplaceItem("$head", Head(pk, 1), new TransactionalBatchItemRequestOptions { IfMatchEtag = "missing-etag" }).ExecuteAsync();
        AssertStatuses(result, HttpStatusCode.NotFound, HttpStatusCode.FailedDependency, HttpStatusCode.NotFound);
        Assert.Equal(0, await CountAsync(c, pk));
    }

    [SkippableTheory]
    [InlineData(100)]
    [InlineData(101)]
    public async Task BatchOperationLimitIncludesPatch(int operations)
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var c = await CreateContainerAsync(db);
        const string pk = "svc|Limit:batch";
        var original = await c.CreateItemAsync(Head(pk), new PartitionKey(pk));
        var batch = c.CreateTransactionalBatch(new PartitionKey(pk));
        for (var i = 1; i < operations; i++) batch.CreateItem(Row(pk, i));
        batch.PatchItem("$head", SetPosition(operations - 1), Compare(0));
        // SDK 3.57.1 sends 101 operations; a future client-side rejection would still need to prove no writes.
        using var response = await batch.ExecuteAsync();
        Assert.Equal(operations == 100 ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(operations == 100 ? 100 : 1, await CountAsync(c, pk));
        Assert.Equal(Position(operations == 100 ? 99 : 0), await ReadPositionAsync(c, pk));
        if (operations == 100)
            Assert.Equal(Enumerable.Repeat(HttpStatusCode.Created, 99).Append(HttpStatusCode.OK), response.Select(r => r.StatusCode));
        else
            Assert.Equal(original.ETag, (await c.ReadItemAsync<JObject>("$head", new PartitionKey(pk))).ETag);
    }

    [SkippableFact]
    public async Task OrderByAndCountIncludeHeadWithoutRowsPredicate()
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var c = await CreateContainerAsync(db);
        const string pk = "svc|Student:query";
        await c.CreateItemAsync(Head(pk), new PartitionKey(pk));
        for (var i = 1; i <= 3; i++) await c.CreateItemAsync(Row(pk, i), new PartitionKey(pk));
        foreach (var direction in new[] { "ASC", "DESC" })
        {
            var docs = await QueryAsync<JObject>(c, $"SELECT * FROM c WHERE c.pk = @pk ORDER BY c.sortableUniqueId {direction}", pk);
            Assert.Equal(4, docs.Count);
            Assert.Single(docs.Where(d => d["id"]!.ToString() == "$head"));
            var rows = await QueryAsync<JObject>(c, $"SELECT * FROM c WHERE c.pk = @pk AND NOT IS_DEFINED(c.documentType) ORDER BY c.sortableUniqueId {direction}", pk);
            Assert.Equal(3, rows.Count);
            Assert.DoesNotContain(rows, d => d["id"]!.ToString() == "$head");
        }
        Assert.Equal(4, await CountAsync(c, pk));
        Assert.Equal(3, Assert.Single(await QueryAsync<int>(c,
            "SELECT VALUE COUNT(1) FROM c WHERE c.pk = @pk AND NOT IS_DEFINED(c.documentType)", pk)));
    }
}
