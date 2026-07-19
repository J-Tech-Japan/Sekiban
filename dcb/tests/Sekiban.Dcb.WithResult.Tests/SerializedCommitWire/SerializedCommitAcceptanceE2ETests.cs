using System.Text;
using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     SEK-G17 no-migration evidence: a literal UNVERSIONED official-shape wire byte string (frozen below, the exact bytes a
///     10.1.x producer emits for registered events) is accepted by the new acceptance surface and committed by the REAL
///     executor + event store, with heterogeneous per-event tags and decoded payload bytes preserved end-to-end — no
///     envelope, no migration, no per-commit-tag translation. The identical bytes with a leading <c>"version":1</c> produce
///     the same committed shape, proving the legacy lift to V1 and the versioned path converge on one executor.
/// </summary>
public class SerializedCommitAcceptanceE2ETests
{
    private const string Guid1 = "11111111-1111-1111-1111-111111111111";
    private const string Guid2 = "22222222-2222-2222-2222-222222222222";

    // Frozen literal unversioned official shape (camelCase, base64 payloads of StudentCreated). Event #1 carries two tags,
    // event #2 one tag — heterogeneous, not flattened.
    private const string LiteralUnversioned =
        """{"eventCandidates":[{"payload":"eyJzdHVkZW50SWQiOiIxMTExMTExMS0xMTExLTExMTEtMTExMS0xMTExMTExMTExMTEiLCJuYW1lIjoiQWxpY2UiLCJtYXhDbGFzc0NvdW50Ijo1fQ==","eventPayloadName":"StudentCreated","tags":["Student:11111111-1111-1111-1111-111111111111","Student:22222222-2222-2222-2222-222222222222"]},{"payload":"eyJzdHVkZW50SWQiOiIyMjIyMjIyMi0yMjIyLTIyMjItMjIyMi0yMjIyMjIyMjIyMjIiLCJuYW1lIjoiQm9iIiwibWF4Q2xhc3NDb3VudCI6M30=","eventPayloadName":"StudentCreated","tags":["Student:22222222-2222-2222-2222-222222222222"]}],"consistencyTags":[]}""";

    private const string LiteralVersionedV1 =
        """{"version":1,"eventCandidates":[{"payload":"eyJzdHVkZW50SWQiOiIxMTExMTExMS0xMTExLTExMTEtMTExMS0xMTExMTExMTExMTEiLCJuYW1lIjoiQWxpY2UiLCJtYXhDbGFzc0NvdW50Ijo1fQ==","eventPayloadName":"StudentCreated","tags":["Student:11111111-1111-1111-1111-111111111111","Student:22222222-2222-2222-2222-222222222222"]},{"payload":"eyJzdHVkZW50SWQiOiIyMjIyMjIyMi0yMjIyLTIyMjItMjIyMi0yMjIyMjIyMjIyMjIiLCJuYW1lIjoiQm9iIiwibWF4Q2xhc3NDb3VudCI6M30=","eventPayloadName":"StudentCreated","tags":["Student:22222222-2222-2222-2222-222222222222"]}],"consistencyTags":[]}""";

    private static ISerializedSekibanDcbExecutor CreateRealExecutor(DcbDomainTypes domainTypes) =>
        (ISerializedSekibanDcbExecutor)new InMemoryDcbExecutorForTesting(
            domainTypes, new Sekiban.Dcb.Testing.InMemoryEventStore(domainTypes.EventTypes));

    [Fact]
    public async Task LiteralUnversionedBytes_ThroughAcceptor_CommitViaRealExecutor_PreservesTagsAndPayloadBytes()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var acceptor = new SerializedCommitAcceptor(CreateRealExecutor(domainTypes));

        var result = await acceptor.AcceptAsync(Encoding.UTF8.GetBytes(LiteralUnversioned));

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        var commit = result.GetValue();
        Assert.Equal(2, commit.WrittenEvents.Count);

        // Heterogeneous per-event tags preserved onto each written event (not flattened across events).
        Assert.Equal(new[] { $"Student:{Guid1}", $"Student:{Guid2}" }, commit.WrittenEvents[0].Tags);
        Assert.Equal(new[] { $"Student:{Guid2}" }, commit.WrittenEvents[1].Tags);

        // Decoded payload bytes preserved byte-for-byte against a freshly-serialized reference.
        byte[] Ref(object o) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o, domainTypes.JsonSerializerOptions));
        Assert.Equal(Ref(new StudentCreated(Guid.Parse(Guid1), "Alice", 5)), commit.WrittenEvents[0].Payload);
        Assert.Equal(Ref(new StudentCreated(Guid.Parse(Guid2), "Bob", 3)), commit.WrittenEvents[1].Payload);

        Assert.Equal("StudentCreated", commit.WrittenEvents[0].EventPayloadName);
    }

    [Fact]
    public async Task ExplicitV1EnvelopeBytes_ProduceTheSameCommitShapeAsLegacy()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var legacy = (await new SerializedCommitAcceptor(CreateRealExecutor(domainTypes))
            .AcceptAsync(Encoding.UTF8.GetBytes(LiteralUnversioned))).GetValue();
        var envelope = (await new SerializedCommitAcceptor(CreateRealExecutor(domainTypes))
            .AcceptAsync(Encoding.UTF8.GetBytes(LiteralVersionedV1))).GetValue();

        // Same written-event shape (payload bytes, tags, type names) whether or not a version was present — EventIds /
        // SortableIds are server-generated per call, so compare the stable projection.
        static object[] Shape(SerializedCommitResult r) =>
            r.WrittenEvents
                .Select(e => (object)(e.EventPayloadName, string.Join(",", e.Tags), Convert.ToBase64String(e.Payload)))
                .ToArray();
        Assert.Equal(Shape(legacy), Shape(envelope));
    }
}
