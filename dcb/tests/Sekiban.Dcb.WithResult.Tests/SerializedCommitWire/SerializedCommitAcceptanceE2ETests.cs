using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dcb.Domain;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.LegacyConsumerFixture;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     SEK-G17 BEHAVIORAL no-migration evidence driven by an INDEPENDENTLY FROZEN dcb-v10.1.17 literal.
///     <para>
///         The authority is a committed, embedded UTF-8 JSON resource
///         (<c>SerializedCommitWire/goldens/legacy_1017_unversioned.json</c>, provenance + pinned SHA-256 in
///         <c>goldens/PROVENANCE.md</c>). Tests read those EXACT committed bytes — never regenerated from current DTOs —
///         and drive them straight through the public <see cref="SerializedCommitAcceptor" /> into a REAL executor + event
///         store, asserting exact decoded payload bytes, distinct heterogeneous per-event tags, result semantics, and
///         empty-request compatibility. Expectations are decoded FROM the frozen literal, so producer and expectation
///         cannot drift together.
///     </para>
///     <para>
///         A drift guard asserts the current-DTO producer (<see cref="Legacy1017WireConsumer" />) still emits bytes equal
///         to the frozen literal: a change to the DTO JSON shape or producer output would diverge from the committed bytes
///         and fail here — the literal expectation can never silently update itself.
///     </para>
/// </summary>
public class SerializedCommitAcceptanceE2ETests
{
    private const string UnversionedSha256 = "26c103ab7c8f117de809711a7b31f26d37ef374c1e551bc1ad0948e4105a17cf";
    private const int UnversionedLength = 531;
    private const string EmptySha256 = "52d11a52f7657262da74a613935aad9cdda2cf4f2191b3d838b56c3e2aa85439";
    private const int EmptyLength = 43;

    private static byte[] LoadFrozen(string fileName)
    {
        var asm = typeof(SerializedCommitAcceptanceE2ETests).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static ISerializedSekibanDcbExecutor CreateRealExecutor(DcbDomainTypes domainTypes) =>
        (ISerializedSekibanDcbExecutor)new InMemoryDcbExecutorForTesting(
            domainTypes, new Sekiban.Dcb.Testing.InMemoryEventStore(domainTypes.EventTypes));

    [Fact]
    public void FrozenLiteralResources_MatchPinnedHash_Length_And_HaveNoBom()
    {
        var unversioned = LoadFrozen("legacy_1017_unversioned.json");
        Assert.Equal(UnversionedLength, unversioned.Length);
        Assert.Equal(UnversionedSha256, Sha256(unversioned));
        Assert.False(unversioned.Length >= 3 && unversioned[0] == 0xEF && unversioned[1] == 0xBB && unversioned[2] == 0xBF);

        var empty = LoadFrozen("legacy_1017_empty.json");
        Assert.Equal(EmptyLength, empty.Length);
        Assert.Equal(EmptySha256, Sha256(empty));
    }

    [Fact]
    public async Task FrozenLiteralBytes_ThroughAcceptor_RealExecutor_PreservesTagsAndPayloadBytes()
    {
        var frozen = LoadFrozen("legacy_1017_unversioned.json");

        // Expectations decoded FROM the frozen literal (independent of current DTO construction).
        using var doc = JsonDocument.Parse(frozen);
        var cands = doc.RootElement.GetProperty("eventCandidates");
        string[] Tags(int i) => cands[i].GetProperty("tags").EnumerateArray().Select(e => e.GetString()!).ToArray();
        byte[] Payload(int i) => Convert.FromBase64String(cands[i].GetProperty("payload").GetString()!);
        string Name(int i) => cands[i].GetProperty("eventPayloadName").GetString()!;

        var domainTypes = DomainType.GetDomainTypes();
        var result = await new SerializedCommitAcceptor(CreateRealExecutor(domainTypes)).AcceptAsync(frozen);

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        var commit = result.GetValue();
        Assert.Equal(2, commit.WrittenEvents.Count);
        Assert.Equal(Tags(0), commit.WrittenEvents[0].Tags);   // heterogeneous per-event tags, not flattened
        Assert.Equal(Tags(1), commit.WrittenEvents[1].Tags);
        Assert.Equal(Payload(0), commit.WrittenEvents[0].Payload); // exact decoded payload bytes from the frozen literal
        Assert.Equal(Payload(1), commit.WrittenEvents[1].Payload);
        Assert.Equal(Name(0), commit.WrittenEvents[0].EventPayloadName);
        Assert.Equal(Name(1), commit.WrittenEvents[1].EventPayloadName);
    }

    [Fact]
    public async Task FrozenEmptyRequestBytes_ThroughAcceptor_AreAcceptedAsEmptyCommit()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var result = await new SerializedCommitAcceptor(CreateRealExecutor(domainTypes))
            .AcceptAsync(LoadFrozen("legacy_1017_empty.json"));

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Empty(result.GetValue().WrittenEvents);
    }

    [Fact]
    public void CurrentProducerOutput_EqualsFrozenLiteral_DriftGuard()
    {
        // Mutation-relevant: the committed literal is the sole authority. If the current DTO JSON shape or the consumer's
        // output changes, the producer diverges from the frozen bytes and THIS assertion fails — the expectation cannot
        // update itself automatically.
        Assert.Equal(LoadFrozen("legacy_1017_unversioned.json"), Legacy1017WireConsumer.SerializeUnversionedWire());
        Assert.Equal(LoadFrozen("legacy_1017_empty.json"), Legacy1017WireConsumer.SerializeEmptyUnversionedWire());
    }

    [Fact]
    public async Task ExplicitV1FromFrozenLiteral_ProducesTheSameCommitShapeAsUnversioned()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var frozen = LoadFrozen("legacy_1017_unversioned.json");
        var versioned = Encoding.UTF8.GetBytes("{\"version\":1," + Encoding.UTF8.GetString(frozen).Substring(1));

        var legacy = (await new SerializedCommitAcceptor(CreateRealExecutor(domainTypes)).AcceptAsync(frozen)).GetValue();
        var envelope = (await new SerializedCommitAcceptor(CreateRealExecutor(domainTypes)).AcceptAsync(versioned)).GetValue();

        static object[] Shape(SerializedCommitResult r) =>
            r.WrittenEvents
                .Select(e => (object)(e.EventPayloadName, string.Join(",", e.Tags), Convert.ToBase64String(e.Payload)))
                .ToArray();
        Assert.Equal(Shape(legacy), Shape(envelope));
    }
}
