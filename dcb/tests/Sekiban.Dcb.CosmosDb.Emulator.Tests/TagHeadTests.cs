using Sekiban.Dcb.Tests.Cosmos;
namespace Sekiban.Dcb.CosmosDb.Emulator.Tests;

[Collection(CosmosEmulatorCollection.Name)]
public sealed class TagHeadTests(CosmosEmulatorFixture fixture)
{
    [SkippableTheory]
    [InlineData(1, false, false)]
    [InlineData(105, false, false)]
    [InlineData(105, true, true)]
    [InlineData(105, true, false)]
    public async Task WritesMaintainHead(int count, bool conflict, bool existingHead)
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        using var scenario = new CosmosTagHeadScenarios(db.Client, db.Name);
        await scenario.WritesAsync(count, conflict, existingHead);
    }
    [SkippableTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task OldRepairAndFallbackBootstrapTop(bool fallback)
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        using var scenario = new CosmosTagHeadScenarios(db.Client, db.Name);
        await scenario.OldRepairAsync(fallback);
    }
    [SkippableFact]
    public async Task ConcurrentBootstrap()
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        using var scenario = new CosmosTagHeadScenarios(db.Client, db.Name);
        await scenario.ConcurrentAsync();
    }
    [SkippableFact]
    public async Task OffCreatesNoHead()
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        using var scenario = new CosmosTagHeadScenarios(db.Client, db.Name, new());
        await scenario.WritesAsync(3, false, false);
    }
}
