using Dcb.Domain;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.LegacyConsumerFixture;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     SEK-G17 BEHAVIORAL no-migration evidence. The wire bytes are produced by the SEPARATE
///     <c>Sekiban.Dcb.LegacyConsumerFixture</c> assembly (a dcb-v10.1.17-era producer built only on pre-G17 public
///     surfaces) — NOT reconstructed in this test assembly — and then crossed into the new acceptance surface and committed
///     by the REAL executor + event store. It asserts exact decoded payload bytes, distinct heterogeneous per-event tags,
///     result semantics, and empty-request compatibility. That the old producer's artifact still commits unchanged is the
///     no-migration proof.
/// </summary>
public class SerializedCommitAcceptanceE2ETests
{
    private static ISerializedSekibanDcbExecutor CreateRealExecutor(DcbDomainTypes domainTypes) =>
        (ISerializedSekibanDcbExecutor)new InMemoryDcbExecutorForTesting(
            domainTypes, new Sekiban.Dcb.Testing.InMemoryEventStore(domainTypes.EventTypes));

    [Fact]
    public async Task LegacyProducerBytes_CrossBoundary_CommitViaRealExecutor_PreservesTagsAndPayloadBytes()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var acceptor = new SerializedCommitAcceptor(CreateRealExecutor(domainTypes));

        // The old producer's artifact — bytes emitted by the separate legacy assembly.
        var producedBytes = Legacy1017WireConsumer.SerializeUnversionedWire();
        var result = await acceptor.AcceptAsync(producedBytes);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        var commit = result.GetValue();
        Assert.Equal(2, commit.WrittenEvents.Count);

        // Heterogeneous per-event tags preserved onto each written event (not flattened across events).
        Assert.Equal(Legacy1017WireConsumer.Event1Tags, commit.WrittenEvents[0].Tags);
        Assert.Equal(Legacy1017WireConsumer.Event2Tags, commit.WrittenEvents[1].Tags);

        // Exact decoded payload bytes preserved byte-for-byte from the producer's artifact.
        Assert.Equal(Legacy1017WireConsumer.Payload1(), commit.WrittenEvents[0].Payload);
        Assert.Equal(Legacy1017WireConsumer.Payload2(), commit.WrittenEvents[1].Payload);

        Assert.Equal("StudentCreated", commit.WrittenEvents[0].EventPayloadName);
        Assert.Equal("StudentCreated", commit.WrittenEvents[1].EventPayloadName);
    }

    [Fact]
    public async Task LegacyProducerEmptyRequestBytes_CrossBoundary_AreAcceptedAsEmptyCommit()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var acceptor = new SerializedCommitAcceptor(CreateRealExecutor(domainTypes));

        var result = await acceptor.AcceptAsync(Legacy1017WireConsumer.SerializeEmptyUnversionedWire());

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Empty(result.GetValue().WrittenEvents);
    }

    [Fact]
    public async Task ExplicitV1EnvelopeBytes_ProduceTheSameCommitShapeAsLegacyProducerBytes()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var legacyBytes = Legacy1017WireConsumer.SerializeUnversionedWire();
        // Prefix an explicit "version":1 to the same producer bytes (turn the leading '{' into '{"version":1,').
        var versionedBytes = System.Text.Encoding.UTF8.GetBytes(
            "{\"version\":1," + System.Text.Encoding.UTF8.GetString(legacyBytes).Substring(1));

        var legacy = (await new SerializedCommitAcceptor(CreateRealExecutor(domainTypes)).AcceptAsync(legacyBytes)).GetValue();
        var envelope = (await new SerializedCommitAcceptor(CreateRealExecutor(domainTypes)).AcceptAsync(versionedBytes)).GetValue();

        static object[] Shape(SerializedCommitResult r) =>
            r.WrittenEvents
                .Select(e => (object)(e.EventPayloadName, string.Join(",", e.Tags), Convert.ToBase64String(e.Payload)))
                .ToArray();
        Assert.Equal(Shape(legacy), Shape(envelope));
    }
}
