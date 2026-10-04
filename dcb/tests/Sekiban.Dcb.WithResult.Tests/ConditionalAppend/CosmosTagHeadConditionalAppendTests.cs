using System.Security.Cryptography;
using System.Text.Json;
using Dcb.Domain;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Events;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BelowExpectedHead_StoreAcceptsAndStalls_ButCallerGuardPreservesNextClaim(bool guardBeforeAppend)
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
        var nextStore = NewStore();
        var tag = new ConditionalMarkerTag("once");

        // Independent, deterministic positions: no process-shared generator or wall clock.
        var headTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SerializableEvent MarkerAt(string payload, DateTime time) =>
            new Event(new ConditionalMarkerEvent(payload), SortableUniqueId.Generate(time, Guid.Empty),
                    nameof(ConditionalMarkerEvent), Guid.NewGuid(), new EventMetadata("c", "c", "u"),
                    new List<string> { tag.GetTag() })
                .ToSerializableEvent(domain.EventTypes);
        var initial = MarkerAt("initial", headTime);
        var seeded = await ((IConditionalEventStore)firstStore).AppendIfUniqueAsync(
            new ConditionalAppendRequest(TagHeadKey(tag.GetTag(), ""), initial));
        Assert.True(seeded.IsSuccess);
        Assert.Equal(ConditionalAppendStatus.Appended, seeded.GetValue().Status);
        var headResult = await firstStore.GetLatestTagAsync(tag);
        Assert.True(headResult.IsSuccess);
        var expectedHead = headResult.GetValue().LastSortedUniqueId;
        Assert.Equal(initial.SortableUniqueIdValue, expectedHead);
        var key = TagHeadKey(tag.GetTag(), expectedHead);
        var stale = MarkerAt("stale", headTime.AddTicks(-1));
        Assert.True(StringComparer.Ordinal.Compare(stale.SortableUniqueIdValue, expectedHead) < 0);

        void CheckPosition(SerializableEvent singleEvent)
        {
            // Identical guard to the English/Japanese documentation sample.
            if (StringComparer.Ordinal.Compare(singleEvent.SortableUniqueIdValue, expectedHead) <= 0)
                throw new InvalidOperationException("The event position must be strictly greater than expectedHead.");
        }

        if (guardBeforeAppend)
        {
            Assert.Throws<InvalidOperationException>(() => CheckPosition(stale));
            Assert.Single(client.Container(options.EventsContainerName).Items);
        }
        else
        {
            var accepted = await ((IConditionalEventStore)firstStore).AppendIfUniqueAsync(
                new ConditionalAppendRequest(key, stale));
            Assert.True(accepted.IsSuccess);
            Assert.Equal(ConditionalAppendStatus.Appended, accepted.GetValue().Status);
            Assert.Equal(stale.SortableUniqueIdValue, accepted.GetValue().WinnerSortableUniqueId);
            Assert.Equal(2, client.Container(options.EventsContainerName).Items.Count);
        }

        var nextHeadResult = await nextStore.GetLatestTagAsync(tag);
        Assert.True(nextHeadResult.IsSuccess);
        var nextHead = nextHeadResult.GetValue().LastSortedUniqueId;
        Assert.Equal(expectedHead, nextHead); // Accepted lower position does not advance the queried head.
        var nextKey = TagHeadKey(tag.GetTag(), nextHead);
        Assert.Equal(key, nextKey);
        var nextEvent = MarkerAt("next-writer", headTime.AddTicks(1));
        CheckPosition(nextEvent);
        var next = await ((IConditionalEventStore)nextStore).AppendIfUniqueAsync(
            new ConditionalAppendRequest(nextKey, nextEvent));
        if (guardBeforeAppend)
        {
            Assert.True(next.IsSuccess);
            Assert.Equal(ConditionalAppendStatus.Appended, next.GetValue().Status);
            Assert.Equal(nextEvent.SortableUniqueIdValue,
                (await nextStore.GetLatestTagAsync(tag)).GetValue().LastSortedUniqueId);
        }
        else
        {
            Assert.False(next.IsSuccess);
            Assert.IsType<KeyReuseConflictException>(next.GetException());
            Assert.Equal(expectedHead, (await nextStore.GetLatestTagAsync(tag)).GetValue().LastSortedUniqueId);
        }
        Assert.Equal(2, client.Container(options.EventsContainerName).Items.Count);
        Assert.Equal(2, (await nextStore.ReadSerializableEventsByTagAsync(tag)).GetValue().Count());
    }

}
