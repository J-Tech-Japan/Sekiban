using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.LegacyConsumerFixture;

/// <summary>
///     A SEPARATE producer/consumer written the way a dcb-v10.1.17 program did, compiled against the CURRENT Sekiban
///     assemblies. It uses ONLY the pre-SEK-G17 public surfaces — the positional <see cref="SerializedCommitRequest" />
///     with the heterogeneous per-event tags model (<see cref="SerializableEventCandidate.Tags" />) and
///     <see cref="ConsistencyTagEntry" /> — and serializes the request to the unversioned official wire shape with plain
///     System.Text.Json web defaults, exactly as a 10.1.17 producer did. It never references the SEK-G17 envelope,
///     acceptor, adapter, or contract serializer. That this still COMPILES (and needs no envelope/version) is the
///     no-migration proof for the 10.1.x line; if any of those surfaces changed, this project would fail to build in CI.
/// </summary>
public static class Legacy1017WireConsumer
{
    /// <summary>Builds the 10.1.17 positional request with heterogeneous per-event tags (2 tags on one event, 1 on the other).</summary>
    public static SerializedCommitRequest BuildRequestWithPerEventTags() =>
        new(
            new List<SerializableEventCandidate>
            {
                new(new byte[] { 1, 2, 3 }, "OrderCreated", new List<string> { "Order:o-1", "Region:south" }),
                new(new byte[] { 4 }, "OrderShipped", new List<string> { "Order:o-1" })
            },
            new List<ConsistencyTagEntry> { new("Order:o-1", "") });

    /// <summary>Serializes to the unversioned official wire shape the way a 10.1.17 producer did (web defaults, camelCase).</summary>
    public static byte[] SerializeUnversionedWire() =>
        JsonSerializer.SerializeToUtf8Bytes(
            BuildRequestWithPerEventTags(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
