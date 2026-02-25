using System.Text.Json;
using Sekiban.Dcb.ColdEvents;
namespace Sekiban.Dcb.ColdEvents.Tests;

public class ColdManifestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void Should_round_trip_serialize_and_deserialize()
    {
        // Given
        var manifest = new ColdManifest(
            ServiceId: "svc-1",
            ManifestVersion: "v1",
            LatestSafeSortableUniqueId: "063923136000000000000000000000",
            Segments:
            [
                new ColdSegmentInfo(
                    Path: "segments/svc-1/seg1.jsonl",
                    FromSortableUniqueId: "063923136000000000000000000000",
                    ToSortableUniqueId: "063923136000000000099999999999",
                    EventCount: 100,
                    SizeBytes: 4096,
                    Sha256: "abc123",
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        // When
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ColdManifest>(json, JsonOptions);

        // Then
        Assert.NotNull(deserialized);
        Assert.Equal("svc-1", deserialized.ServiceId);
        Assert.Equal("v1", deserialized.ManifestVersion);
        Assert.Equal("063923136000000000000000000000", deserialized.LatestSafeSortableUniqueId);
        Assert.Single(deserialized.Segments);
        Assert.Equal(100, deserialized.Segments[0].EventCount);
    }

    [Fact]
    public void Should_deserialize_manifest_to_get_stored_position()
    {
        // Given: stored manifest JSON
        var manifest = new ColdManifest(
            ServiceId: "svc-1",
            ManifestVersion: "v2",
            LatestSafeSortableUniqueId: "063923136000000000050000000000",
            Segments:
            [
                new ColdSegmentInfo(
                    Path: "segments/svc-1/seg1.jsonl",
                    FromSortableUniqueId: "063923136000000000000000000000",
                    ToSortableUniqueId: "063923136000000000025000000000",
                    EventCount: 50_000,
                    SizeBytes: 2048,
                    Sha256: "hash1",
                    CreatedAtUtc: DateTimeOffset.UtcNow),
                new ColdSegmentInfo(
                    Path: "segments/svc-1/seg2.jsonl",
                    FromSortableUniqueId: "063923136000000000025000000001",
                    ToSortableUniqueId: "063923136000000000050000000000",
                    EventCount: 50_000,
                    SizeBytes: 2048,
                    Sha256: "hash2",
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

        // When
        var restored = JsonSerializer.Deserialize<ColdManifest>(json, JsonOptions);

        // Then: can determine stored position from manifest alone
        Assert.NotNull(restored);
        Assert.Equal("063923136000000000050000000000", restored.LatestSafeSortableUniqueId);
        Assert.Equal(2, restored.Segments.Count);
        Assert.Equal(100_000, restored.Segments.Sum(s => s.EventCount));
    }
}
