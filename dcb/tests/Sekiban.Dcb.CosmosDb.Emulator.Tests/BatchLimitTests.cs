using System.Net;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Sekiban.Dcb.CosmosDb.Emulator.Tests;

[Collection(CosmosEmulatorCollection.Name)]
public sealed class BatchLimitTests(CosmosEmulatorFixture fixture)
{
    [SkippableTheory]
    [InlineData(100)]
    [InlineData(101)]
    public async Task TransactionalBatchCreateLimit(int operations)
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var container = (await db.Database.CreateContainerAsync("tags", "/pk")).Container;
        const string pk = "svc|Student:batch";
        var batch = container.CreateTransactionalBatch(new PartitionKey(pk));
        for (var i = 0; i < operations; i++)
            batch.CreateItem(new JObject { ["id"] = $"row-{i}", ["pk"] = pk });

        using var response = await batch.ExecuteAsync();
        Assert.Equal(operations == 100 ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
        if (operations == 100)
            Assert.All(response, operation => Assert.Equal(HttpStatusCode.Created, operation.StatusCode));
        Assert.Equal(operations == 100 ? 100 : 0,
            Assert.Single(await RealStoreTests.QueryAsync<int>(container, "SELECT VALUE COUNT(1) FROM c")));
    }
}
