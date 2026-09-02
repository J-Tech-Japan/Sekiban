using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     C# runner tests for SEK-G52. They make the directional R1/R2/R3 boundaries executable without teaching the
///     production acceptor to consume the TypeScript-client dialect.
/// </summary>
public sealed class SerializedCommitInteropFixtureTests
{
    [Fact]
    public void CSharpRunner_VerifiesFrozenProvenance_AndTheThreeDirectionalContracts()
    {
        var manifest = SerializedCommitInteropFixtureRunner.LoadManifest();
        var descriptors = SerializedCommitInteropFixtureRunner.VerifyFrozenResources();

        Assert.Equal("f53ffdc69e225433b266cc1f92875d6b2b11aa93", manifest.SourceCommit);
        Assert.Equal("runtime-v1", manifest.ContractVersion);
        Assert.Contains("eventId", manifest.ExcludedMembers);
        Assert.Equal(15, descriptors.Count);

        // R1: only the official V1 envelope meeting the stated payload constraints claims byte equality.
        SerializedCommitInteropFixtureRunner.VerifyR1RuntimePayloadRoundTrip(
            SerializedCommitInteropFixtureRunner.LoadFixture("interop_official_v1_populated.json"));

        // R2 positive: client eventId is deliberately excluded and the adapter output is exactly the frozen official V1.
        Assert.Equal(
            SerializedCommitInteropFixtureRunner.LoadFixture("interop_official_v1_populated.json"),
            SerializedCommitInteropFixtureRunner.ConvertClientModelToCanonicalV1(
                SerializedCommitInteropFixtureRunner.LoadFixture("interop_ts_client_model.json")));
        Assert.Equal(
            SerializedCommitInteropFixtureRunner.LoadFixture("interop_r2_canonical_positive_v1.json"),
            SerializedCommitInteropFixtureRunner.ConvertClientModelToCanonicalV1(
                SerializedCommitInteropFixtureRunner.LoadFixture("interop_r2_canonical_positive.json")));

        // The discriminating pair documents observed JavaScript loss rather than claiming equality.
        Assert.Equal(
            "{\"2\":2,\"z\":1,\"a\":3}",
            SerializedCommitInteropFixtureRunner.FirstCanonicalPayloadText(
                SerializedCommitInteropFixtureRunner.ConvertClientModelToCanonicalV1(
                    SerializedCommitInteropFixtureRunner.LoadFixture("interop_r2_integer_like_key.json"))));
        Assert.Equal(
            "{\"one\":1,\"exp\":100,\"negativeZero\":0,\"large\":9007199254740992}",
            SerializedCommitInteropFixtureRunner.FirstCanonicalPayloadText(
                SerializedCommitInteropFixtureRunner.ConvertClientModelToCanonicalV1(
                    SerializedCommitInteropFixtureRunner.LoadFixture("interop_r2_numeric_lexical_loss.json"))));

        SerializedCommitInteropFixtureRunner.ExpectClientBindError(
            SerializedCommitInteropFixtureRunner.LoadFixture("interop_r2_duplicate_key.json"),
            "duplicate-json-key");
        SerializedCommitInteropFixtureRunner.ExpectClientBindError(
            SerializedCommitInteropFixtureRunner.LoadFixture("interop_client_empty_tag.json"),
            "empty-tag");
        SerializedCommitInteropFixtureRunner.ExpectClientBindError(
            SerializedCommitInteropFixtureRunner.LoadFixture("interop_client_duplicate_consistency.json"),
            "duplicate-consistency");

        // R3: server payload bytes remain legal server input, but no client-model equality is attempted for these values.
        SerializedCommitInteropFixtureRunner.ExpectR3PayloadBindError(
            SerializedCommitInteropFixtureRunner.LoadFixture("interop_r3_bom_payload.json"),
            "bom-prefixed-payload");
        SerializedCommitInteropFixtureRunner.ExpectR3PayloadBindError(
            SerializedCommitInteropFixtureRunner.LoadFixture("interop_r3_non_json_payload.json"),
            "non-json-payload");
        SerializedCommitInteropFixtureRunner.ExpectR3PayloadBindError(
            SerializedCommitInteropFixtureRunner.LoadFixture("interop_r3_invalid_utf8_payload.json"),
            "invalid-utf8-payload");
    }

    [Fact]
    public void CSharpRunner_RejectsAProvenanceDigestMismatch()
    {
        var bytes = SerializedCommitInteropFixtureRunner.LoadFixture("interop_official_v1_populated.json");
        var mismatch = Assert.Throws<InvalidOperationException>(() =>
            SerializedCommitInteropFixtureRunner.VerifyDigest(bytes, bytes.Length, new string('0', 64)));

        Assert.Contains("SHA-256 mismatch", mismatch.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("interop_official_v1_populated.json", 2)]
    [InlineData("interop_legacy_populated.json", 2)]
    [InlineData("interop_legacy_explicit_empty.json", 0)]
    public async Task FrozenOfficialAndLegacyFixtures_AreBoundByTheCSharpAcceptor(
        string fixtureName,
        int expectedCandidates)
    {
        var executor = new RecordingExecutor();
        var result = await new SerializedCommitAcceptor(executor).AcceptAsync(
            SerializedCommitInteropFixtureRunner.LoadFixture(fixtureName));

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.GetException().ToString());
        Assert.Equal(1, executor.CommitCalls);
        Assert.NotNull(executor.LastRequest);
        Assert.Equal(expectedCandidates, executor.LastRequest!.EventCandidates.Count);
    }

    [Fact]
    public async Task EveryClientShapedFrozenFixture_IsRejectedByTheSekG51ClosedAliasGate()
    {
        var manifest = SerializedCommitInteropFixtureRunner.LoadManifest();
        foreach (var fixture in manifest.Fixtures.Where(fixture => fixture.ClientShaped))
        {
            var executor = new RecordingExecutor();
            var result = await new SerializedCommitAcceptor(executor).AcceptAsync(
                SerializedCommitInteropFixtureRunner.LoadFixture(fixture.File));

            var error = Assert.IsType<MalformedSerializedCommitException>(result.GetException());
            Assert.Equal(SerializedCommitShapeError.AliasedCollectionMember, error.Reason);
            Assert.Equal(0, executor.CommitCalls);
        }
    }

    private sealed class RecordingExecutor : ISerializedSekibanDcbExecutor
    {
        public int CommitCalls { get; private set; }
        public SerializedCommitRequest? LastRequest { get; private set; }

        public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
            throw new NotSupportedException();

        public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
            SerializedCommitRequest request,
            CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            LastRequest = request;
            return Task.FromResult(ResultBox.FromValue(
                new SerializedCommitResult(
                    Array.Empty<SerializableEvent>(),
                    Array.Empty<TagWriteResult>(),
                    TimeSpan.Zero)));
        }
    }
}
