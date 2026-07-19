using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Xunit;
namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     SEK-G17 legacy unversioned adapter: the version-less official shape is lifted losslessly to V1. Event candidates and
///     consistency tags — including each event's heterogeneous per-event tags — are carried through verbatim, and no
///     per-commit-tag model is involved.
/// </summary>
public class LegacyUnversionedSerializedCommitAdapterTests
{
    [Fact]
    public void ToVersionedV1_SetsCurrentVersion_AndCarriesPayloadVerbatim()
    {
        var legacy = new SerializedCommitRequest(
            new List<SerializableEventCandidate>
            {
                new(new byte[] { 1, 2, 3 }, "E1", new List<string> { "G:a", "H:b" }),
                new(new byte[] { 9 }, "E2", new List<string> { "G:a" })
            },
            new List<ConsistencyTagEntry> { new("G:a", "sid-1") });

        var v1 = LegacyUnversionedSerializedCommitAdapter.ToVersionedV1(legacy);

        Assert.Equal(VersionedSerializedCommitRequest.CurrentVersion, v1.Version);
        Assert.Same(legacy.EventCandidates, v1.EventCandidates);   // no copy / no per-commit-tag translation
        Assert.Same(legacy.ConsistencyTags, v1.ConsistencyTags);
        // Heterogeneous per-event tags preserved (2 tags on event #1, 1 tag on event #2).
        Assert.Equal(new[] { "G:a", "H:b" }, v1.EventCandidates[0].Tags);
        Assert.Equal(new[] { "G:a" }, v1.EventCandidates[1].Tags);
    }

    [Fact]
    public void ToVersionedV1_PreservesEmptyRequest()
    {
        var legacy = new SerializedCommitRequest(
            Array.Empty<SerializableEventCandidate>(), Array.Empty<ConsistencyTagEntry>());
        var v1 = LegacyUnversionedSerializedCommitAdapter.ToVersionedV1(legacy);
        Assert.Equal(VersionedSerializedCommitRequest.CurrentVersion, v1.Version);
        Assert.Empty(v1.EventCandidates);
        Assert.Empty(v1.ConsistencyTags);
    }
}
