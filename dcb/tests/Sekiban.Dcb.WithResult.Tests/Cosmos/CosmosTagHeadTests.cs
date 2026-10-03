using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Tags;

namespace Sekiban.Dcb.Tests.Cosmos;

public sealed class CosmosTagHeadTests
{
    [Theory]
    [InlineData(1, false, false)]
    [InlineData(105, false, false)]
    [InlineData(105, true, true)]
    [InlineData(105, true, false)]
    public async Task WritesMaintainHead(int count, bool conflict, bool existingHead)
    {
        var client = new InMemoryCosmosClient();
        using var scenario = new CosmosTagHeadScenarios(client, "test");
        await scenario.WritesAsync(count, conflict, existingHead);
        var batches = client.Container("tags").BatchInventory;
        Assert.All(batches, batch => Assert.InRange(batch.Length, 1, 100));
        Assert.StartsWith("Patch:$head", batches[0][0]);
        if (count > 99) Assert.Contains(batches, b => b.All(op => !op.EndsWith("$head", StringComparison.Ordinal)));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RolledBackFirstChunkConfirmsWholeMaximumBeforeAnyRow(bool headExists)
    {
        var tags = new InMemoryCosmosContainer("tags");
        var sources = Enumerable.Range(1, 105).Select(i => new CosmosTagRowSource(
            "Student:1", Guid.NewGuid(), i.ToString("D30"), "event")).ToList();
        var rows = sources.Select(s => CosmosTag.FromEventTag(s.Tag, "Student", s.SortableUniqueId,
            s.EventId, s.EventType, "svc")).ToList();
        tags.Seed(rows[0]);
        var maximum = sources[^1].SortableUniqueId;
        if (headExists) tags.Seed(Head(sources[0].SortableUniqueId));
        tags.OnWrite = _ =>
        {
            var head = Assert.Single(tags.Items, d => d["id"]!.ToString() == "$head");
            Assert.True(string.CompareOrdinal(head["position"]!.ToString(), maximum) >= 0);
        };
        // Bootstrap CreateItem invokes OnWrite before the head itself is inserted; allow that one creation.
        if (!headExists)
        {
            var check = tags.OnWrite;
            tags.OnWrite = n => { if (n > 0) check!(n); };
        }
        await CosmosTagWriteStage.WriteAsync(sources, new CosmosContainerTagRowStore(tags),
            new() { TagHeadMode = CosmosTagHeadMode.Advance }, "svc");
        Assert.Equal(maximum, tags.BatchHeadPositions[0]);
        Assert.Equal(106, tags.Items.Count);
        Assert.Equal(maximum, Assert.Single(tags.Items, d => d["id"]!.ToString() == "$head")["position"]!.ToString());
    }

    private static Newtonsoft.Json.Linq.JObject Head(string position) => new()
    {
        ["id"] = "$head", ["pk"] = "svc|Student:1", ["serviceId"] = "svc",
        ["tag"] = "Student:1", ["documentType"] = "tagHead", ["position"] = position
    };

    [Fact]
    public async Task ConcurrentBootstrapCreateConflictRetriesPatch()
    {
        var tags = new InMemoryCosmosContainer("tags");
        var source = new CosmosTagRowSource("Student:1", Guid.NewGuid(), 10.ToString("D30"), "event");
        tags.BeforeBatch = operations =>
        {
            if (operations[0] == "Create:$head") tags.Seed(Head(20.ToString("D30")));
        };
        await CosmosTagWriteStage.WriteAsync([source], new CosmosContainerTagRowStore(tags),
            new() { TagHeadMode = CosmosTagHeadMode.Advance }, "svc");
        Assert.Equal(new[] { "Patch:$head", "Create:$head", "Patch:$head", "Create:" + source.EventId },
            tags.BatchInventory.Select(b => b[0]));
        Assert.Equal(20.ToString("D30"), tags.Items.Single(d => d["id"]!.ToString() == "$head")["position"]!.ToString());
    }

    [Theory]
    [InlineData("holds", System.Net.HttpStatusCode.OK)]
    [InlineData("fails", System.Net.HttpStatusCode.PreconditionFailed)]
    [InlineData("missing", System.Net.HttpStatusCode.NotFound)]
    [InlineData("conflict", System.Net.HttpStatusCode.Conflict)]
    public async Task FakeHeadAtIndexZeroHasEngineStatusesAndRollback(string scenario, System.Net.HttpStatusCode status)
    {
        var tags = new InMemoryCosmosContainer("tags");
        if (scenario != "missing") tags.Seed(Head((scenario == "fails" ? 20 : 1).ToString("D30")));
        var row = CosmosTag.FromEventTag("Student:1", "Student", 10.ToString("D30"), Guid.NewGuid(), "event", "svc");
        if (scenario == "conflict") tags.Seed(row);
        var before = tags.Items.Count;
        using var response = await tags.CreateTransactionalBatch(new Microsoft.Azure.Cosmos.PartitionKey(row.Pk))
            .PatchItem("$head", CosmosTagHead.PositionPatch(row.SortableUniqueId),
                new Microsoft.Azure.Cosmos.TransactionalBatchPatchItemRequestOptions
                { FilterPredicate = CosmosTagHead.BuildValidatedPositionPredicate(row.SortableUniqueId) })
            .CreateItem(row).ExecuteAsync();
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(status == System.Net.HttpStatusCode.Conflict ? System.Net.HttpStatusCode.FailedDependency : status,
            response[0].StatusCode);
        Assert.Equal(scenario == "holds" ? System.Net.HttpStatusCode.Created : scenario == "conflict"
            ? System.Net.HttpStatusCode.Conflict : System.Net.HttpStatusCode.FailedDependency, response[1].StatusCode);
        Assert.Equal(before + (scenario == "holds" ? 1 : 0), tags.Items.Count);
        if (scenario != "missing") Assert.Equal((scenario == "holds" ? 10 : scenario == "fails" ? 20 : 1).ToString("D30"),
            tags.Items.Single(d => d["id"]!.ToString() == "$head")["position"]!.ToString());
    }

    [Fact]
    public async Task BootstrapRetriesAreBoundedAndNeverCreateRows()
    {
        var tags = new InMemoryCosmosContainer("tags");
        for (var i = 0; i < CosmosTagHead.RetryLimit; i++)
        {
            tags.WriteFaults.Enqueue(CosmosFailures.NotFound());
            tags.WriteFaults.Enqueue(CosmosFailures.Conflict());
        }
        var row = CosmosTag.FromEventTag("Student:1", "Student", 1.ToString("D30"), Guid.NewGuid(), "event", "svc");
        await Assert.ThrowsAsync<Microsoft.Azure.Cosmos.CosmosException>(() => CosmosTagHead.AdvanceHeadAsync(
            tags, row.Pk, row, row.SortableUniqueId, CancellationToken.None));
        Assert.Empty(tags.Items);
        Assert.Empty(tags.WriteFaults);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldRepairAndFallbackBootstrapTop(bool fallback)
    {
        var client = new InMemoryCosmosClient();
        using var scenario = new CosmosTagHeadScenarios(client, "test");
        await scenario.OldRepairAsync(fallback);
    }
    [Fact]
    public async Task ConcurrentBootstrap()
    {
        var client = new InMemoryCosmosClient();
        using var scenario = new CosmosTagHeadScenarios(client, "test");
        await scenario.ConcurrentAsync();
    }
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(101)]
    public async Task BatchBounds(int size)
    {
        foreach (var mode in new[] { CosmosTagHeadMode.Off, CosmosTagHeadMode.Advance })
        {
            var client = new InMemoryCosmosClient();
            var options = new CosmosDbEventStoreOptions { TagHeadMode = mode, MaxBatchOperations = size };
            if (mode == CosmosTagHeadMode.Advance && size < 2)
            {
                Assert.Throws<InvalidOperationException>(() => new CosmosDbContext(client, "test", options: options));
                continue;
            }
            using var scenario = new CosmosTagHeadScenarios(client, "test", options);
            await scenario.WritesAsync(105, false, false);
            var expected = Math.Clamp(size, 1, 100);
            Assert.Equal(expected, client.Container("tags").BatchInventory[0].Length);
            Assert.All(client.Container("tags").BatchInventory, b => Assert.InRange(b.Length, 1, expected));
            if (mode == CosmosTagHeadMode.Off)
                Assert.All(client.Container("tags").BatchInventory, b => Assert.All(b, op => Assert.StartsWith("Create:", op)));
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InvalidPositionRejectedBeforeEvents(bool serialized)
    {
        var client = new InMemoryCosmosClient();
        using var scenario = new CosmosTagHeadScenarios(client, "test");
        await scenario.InvalidPositionAsync(serialized);
        Assert.Empty(client.Container("events").Items);
        Assert.Empty(client.Container("tags").Items);
    }

    [Fact]
    public async Task InvalidAndMutatedOptionsFailBeforeRows()
    {
        var options = new CosmosDbEventStoreOptions { TagHeadMode = CosmosTagHeadMode.Advance, UseTransactionalBatchForTags = false };
        var client = new InMemoryCosmosClient();
        Assert.Throws<InvalidOperationException>(() => new CosmosDbContext(client, "test", options: options));
        options.UseTransactionalBatchForTags = true;
        var store = new CosmosContainerTagRowStore(client.Container("tags"));
        var source = new CosmosTagRowSource("Student:1", Guid.NewGuid(), "' OR true", "event");
        await Assert.ThrowsAsync<ArgumentException>(() => CosmosTagWriteStage.WriteAsync([source], store, options, "svc"));
        Assert.Empty(client.Container("tags").BatchInventory);
        options.MaxBatchOperations = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => CosmosTagWriteStage.WriteAsync([], store, options, "svc"));
    }
}
