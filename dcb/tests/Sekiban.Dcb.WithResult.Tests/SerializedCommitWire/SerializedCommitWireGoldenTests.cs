using System.Text.Json;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Xunit;
using V = Sekiban.Dcb.Tests.SerializedCommitWire.SerializedCommitWireVectors;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     SEK-G17 frozen golden wire vectors for the official serialized-commit contract.
///     <para>
///         PROVENANCE: the wire DTOs are byte-identical between the <c>dcb-v10.1.17</c> tag
///         (commit <c>a90e6197091c3e6958bb7209dfebd6a12ebc6c65</c>) and the current HEAD — verified git blob SHAs:
///         <c>SerializedCommitRequest.cs = b6d5290c…</c>, <c>SerializableEventCandidate.cs = 600efe91…</c>,
///         <c>ConsistencyTagEntry.cs = f4d4f1a2…</c>. Because the types never changed, serializing them under the pinned
///         contract settings reproduces the EXACT 10.1.17 wire bytes; those bytes are frozen (Base64) in
///         <see cref="SerializedCommitWireVectors" /> so any serializer drift (naming, order, encoder, indentation, base64)
///         fails these assertions in CI.
///     </para>
///     The dataset deliberately uses HETEROGENEOUS per-event tags and NON-ASCII tag / event-type names so the encoder
///     (escape non-ASCII) is pinned; payloads are base64 on the wire, so decoded-payload byte equality is asserted
///     separately from the envelope bytes.
/// </summary>
public class SerializedCommitWireGoldenTests
{
    [Fact]
    public void ContractSerializer_ProducesTheFrozenOfficialGolden()
    {
        var bytes = SerializedCommitWireContract.SerializeToUtf8Bytes(V.BuildOfficial());
        Assert.Equal(V.OfficialCamel, bytes); // byte-exact, no BOM
    }

    [Fact]
    public void ContractSerializer_ProducesTheFrozenVersionedGolden()
    {
        var bytes = SerializedCommitWireContract.SerializeToUtf8Bytes(V.BuildVersioned());
        Assert.Equal(V.VersionedV1Camel, bytes);
    }

    [Fact]
    public void LegacyAspNetWebDefaults_ProduceTheSameOfficialGolden_DistinctAssertion()
    {
        // The current (pre-existing) serializer path — ASP.NET JsonSerializerDefaults.Web — is asserted as a SEPARATE
        // vector: it must equal the same frozen 10.1.17 bytes, proving the contract serializer reproduces reality.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(V.BuildOfficial(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(V.OfficialCamel, bytes);
    }

    [Fact]
    public void LegacyAmbientFreshOptions_RemainPascalCase_ByteForByteUnchanged()
    {
        // No attributes were added to the positional DTOs: a fresh JsonSerializerOptions still emits PascalCase.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(V.BuildOfficial(), new JsonSerializerOptions());
        Assert.Equal(V.OfficialFreshPascal, bytes);
    }

    [Fact]
    public void OfficialGolden_RoundTrips_ThroughContractOptions_ByteExact()
    {
        var request = JsonSerializer.Deserialize<SerializedCommitRequest>(V.OfficialCamel, SerializedCommitWireContract.Options);
        Assert.NotNull(request);
        var reserialized = SerializedCommitWireContract.SerializeToUtf8Bytes(request!);
        Assert.Equal(V.OfficialCamel, reserialized);
    }

    [Fact]
    public void AdditiveV2ExpectedPositionEnvelope_RoundTripsWithoutChangingTheFrozenV1Shapes()
    {
        var request = new VersionedExpectedTagPositionSerializedCommitRequest(
            VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion,
            [new SerializableEventCandidate("payload"u8.ToArray(), "Event", ["Marker:m"])],
            [new ConsistencyTagEntry("Marker:m", "previous")],
            [new TagHeadExpectationEntry("default", "Marker:m", TagHeadExpectation.Exact("previous"))]);

        var bytes = SerializedCommitWireContract.SerializeToUtf8Bytes(request);
        var roundTrip = JsonSerializer.Deserialize<VersionedExpectedTagPositionSerializedCommitRequest>(
            bytes,
            SerializedCommitWireContract.Options);

        Assert.NotNull(roundTrip);
        Assert.Equal(VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion, roundTrip.Version);
        Assert.Equal("Event", Assert.Single(roundTrip.EventCandidates).EventPayloadName);
        Assert.Equal("previous", Assert.Single(roundTrip.ConsistencyTags).LastSortableUniqueId);
        var expectation = Assert.Single(roundTrip.ExpectedTagPositions);
        Assert.Equal(TagHeadExpectationKind.Exact, expectation.Expectation.Kind);
        Assert.Equal("previous", expectation.Expectation.Position);
        Assert.Equal(bytes, SerializedCommitWireContract.SerializeToUtf8Bytes(roundTrip));
    }

    [Fact]
    public void DecodedPayloadBytes_AreAssertedSeparatelyFromEnvelopeBytes()
    {
        using var doc = JsonDocument.Parse(V.OfficialCamel);
        var candidates = doc.RootElement.GetProperty("eventCandidates");
        var decoded0 = Convert.FromBase64String(candidates[0].GetProperty("payload").GetString()!);
        var decoded1 = Convert.FromBase64String(candidates[1].GetProperty("payload").GetString()!);
        Assert.Equal(V.Payload1, decoded0);
        Assert.Equal(V.Payload2, decoded1);
    }

    [Fact]
    public void HeterogeneousPerEventTags_ArePreservedOnTheWire()
    {
        using var doc = JsonDocument.Parse(V.OfficialCamel);
        var candidates = doc.RootElement.GetProperty("eventCandidates");
        var tags0 = candidates[0].GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ToArray();
        var tags1 = candidates[1].GetProperty("tags").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "Cliente:José", "Región:Sur" }, tags0); // two tags on event #1
        Assert.Equal(new[] { "Cliente:José" }, tags1);               // one tag on event #2 — not flattened across events
    }
}
