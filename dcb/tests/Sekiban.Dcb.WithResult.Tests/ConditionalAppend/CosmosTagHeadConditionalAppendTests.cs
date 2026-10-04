using System.Security.Cryptography;
using System.Text.Json;
using Dcb.Domain;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.TestSupport;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;

namespace Sekiban.Dcb.Tests.ConditionalAppend;

public class CosmosTagHeadConditionalAppendTests
{
    // Keep this derivation identical to the interim pattern in both storage-provider guides.
    private static string TagHeadKey(string tag, string expectedHead) =>
        "tag-head-v1:" + Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new[] { tag, expectedHead })));

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        public string GetCurrentServiceId() => "svc";
    }

    [Fact]
    public async Task SameTagAndHead_DifferentPayloads_OneWinner_SuccessorWins_IdenticalPayloadAliases()
    {
        var domain = ConditionalAppendScenarios.RegisterMarker(DomainType.GetDomainTypes());
        var client = new InMemoryCosmosClient();
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "events",
            TagsContainerName = "tags",
            WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward
        };
        CosmosDbEventStore NewStore() => new(
            new CosmosDbContext(client, "test-db", null, options), domain.EventTypes,
            new FixedServiceIdProvider(), new DefaultCosmosContainerResolver(options));
        var firstStore = NewStore();
        var secondStore = NewStore();
        var tag = new ConditionalMarkerTag("once");
        var headResult = await firstStore.GetLatestTagAsync(tag);
        Assert.True(headResult.IsSuccess);
        var expectedHead = headResult.GetValue().LastSortedUniqueId;
        Assert.Equal("", expectedHead);
        var key = TagHeadKey(tag.GetTag(), expectedHead);
        var payloads = new[] { "request-a", "request-b" };

        var attempts = await Task.WhenAll(
            ((IConditionalEventStore)firstStore).AppendIfUniqueAsync(
                new ConditionalAppendRequest(key, ConditionalAppendScenarios.Marker(domain, payloads[0]))),
            ((IConditionalEventStore)secondStore).AppendIfUniqueAsync(
                new ConditionalAppendRequest(key, ConditionalAppendScenarios.Marker(domain, payloads[1]))));

        var winnerIndex = Array.FindIndex(attempts, result => result.IsSuccess);
        var winner = Assert.Single(attempts.Where(result => result.IsSuccess)).GetValue();
        Assert.Equal(ConditionalAppendStatus.Appended, winner.Status);
        Assert.IsType<KeyReuseConflictException>(Assert.Single(attempts.Where(result => !result.IsSuccess)).GetException());
        Assert.Single(client.Container(options.EventsContainerName).Items);
        Assert.Equal(winner.WinnerSortableUniqueId,
            (await firstStore.GetLatestTagAsync(tag)).GetValue().LastSortedUniqueId);

        // Fresh event ids do not distinguish identical canonical payloads under the original key.
        var alias = await ((IConditionalEventStore)secondStore).AppendIfUniqueAsync(
            new ConditionalAppendRequest(key, ConditionalAppendScenarios.Marker(domain, payloads[winnerIndex])));
        Assert.True(alias.IsSuccess);
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, alias.GetValue().Status);
        Assert.Equal(winner.WinnerEventId, alias.GetValue().WinnerEventId);
        Assert.Equal(winner.WinnerSortableUniqueId, alias.GetValue().WinnerSortableUniqueId);
        Assert.Single(client.Container(options.EventsContainerName).Items);

        var successorKey = TagHeadKey(tag.GetTag(), winner.WinnerSortableUniqueId);
        Assert.NotEqual(key, successorKey);
        var successor = await ((IConditionalEventStore)secondStore).AppendIfUniqueAsync(
            new ConditionalAppendRequest(successorKey, ConditionalAppendScenarios.Marker(domain, "request-successor")));
        Assert.True(successor.IsSuccess);
        Assert.Equal(ConditionalAppendStatus.Appended, successor.GetValue().Status);
        Assert.Equal(2, client.Container(options.EventsContainerName).Items.Count);
        Assert.Equal(2, (await firstStore.ReadSerializableEventsByTagAsync(tag)).GetValue().Count());
        Assert.Equal(successor.GetValue().WinnerSortableUniqueId,
            (await firstStore.GetLatestTagAsync(tag)).GetValue().LastSortedUniqueId);
    }
}
