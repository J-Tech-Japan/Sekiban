using System.Text.Json;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ColdEvents;
namespace Sekiban.Dcb.ColdEvents.Tests;

public class ColdCatalogReaderTests
{
    private const string ServiceId = "test-service";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly ColdEventStoreOptions EnabledOptions = new()
    {
        Enabled = true,
        PullInterval = TimeSpan.FromMinutes(30),
        SafeWindow = TimeSpan.FromMinutes(2),
        SegmentMaxEvents = 100_000,
        SegmentMaxBytes = 512L * 1024 * 1024
    };

    private readonly InMemoryColdObjectStorage _storage = new();

    private ColdCatalogReader CreateReader(ColdEventStoreOptions? options = null)
    {
        return new ColdCatalogReader(
            _storage,
            Options.Create(options ?? EnabledOptions));
    }

    [Fact]
    public async Task GetStatusAsync_should_return_supported_and_enabled()
    {
        // Given
        var reader = CreateReader();

        // When
        var status = await reader.GetStatusAsync(CancellationToken.None);

        // Then
        Assert.True(status.IsSupported);
        Assert.True(status.IsEnabled);
    }

    [Fact]
    public async Task GetStatusAsync_should_return_supported_but_disabled_when_not_enabled()
    {
        // Given
        var options = new ColdEventStoreOptions { Enabled = false };
        var reader = CreateReader(options);

        // When
        var status = await reader.GetStatusAsync(CancellationToken.None);

        // Then
        Assert.True(status.IsSupported);
        Assert.False(status.IsEnabled);
    }

    [Fact]
    public async Task GetDataRangeSummaryAsync_should_return_empty_when_no_manifest()
    {
        // Given
        var reader = CreateReader();

        // When
        var result = await reader.GetDataRangeSummaryAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        var summary = result.GetValue();
        Assert.Equal(ServiceId, summary.ServiceId);
        Assert.Null(summary.OldestSortableUniqueId);
        Assert.Null(summary.LatestSortableUniqueId);
        Assert.Equal(0, summary.TotalEventCount);
        Assert.Equal(0, summary.SegmentCount);
        Assert.Empty(summary.Segments);
    }

    [Fact]
    public async Task GetDataRangeSummaryAsync_should_return_summary_for_single_segment()
    {
        // Given
        var manifest = new ColdManifest(
            ServiceId: ServiceId,
            ManifestVersion: "v1",
            LatestSafeSortableUniqueId: "id-200",
            Segments:
            [
                new ColdSegmentInfo(
                    Path: "segments/test-service/id-100_id-200.jsonl",
                    FromSortableUniqueId: "id-100",
                    ToSortableUniqueId: "id-200",
                    EventCount: 50,
                    SizeBytes: 1024,
                    Sha256: "abc123",
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        await WriteManifest(manifest);
        var reader = CreateReader();

        // When
        var result = await reader.GetDataRangeSummaryAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        var summary = result.GetValue();
        Assert.Equal(ServiceId, summary.ServiceId);
        Assert.Equal("id-100", summary.OldestSortableUniqueId);
        Assert.Equal("id-200", summary.LatestSortableUniqueId);
        Assert.Equal(50, summary.TotalEventCount);
        Assert.Equal(1, summary.SegmentCount);
        Assert.Single(summary.Segments);
        var seg = summary.Segments[0];
        Assert.Equal("segments/test-service/id-100_id-200.jsonl", seg.Path);
        Assert.Equal("id-100", seg.FromSortableUniqueId);
        Assert.Equal("id-200", seg.ToSortableUniqueId);
        Assert.Equal(50, seg.EventCount);
    }

    [Fact]
    public async Task GetDataRangeSummaryAsync_should_return_summary_for_multiple_segments()
    {
        // Given
        var manifest = new ColdManifest(
            ServiceId: ServiceId,
            ManifestVersion: "v2",
            LatestSafeSortableUniqueId: "id-400",
            Segments:
            [
                new ColdSegmentInfo(
                    Path: "segments/test-service/id-100_id-200.jsonl",
                    FromSortableUniqueId: "id-100",
                    ToSortableUniqueId: "id-200",
                    EventCount: 50,
                    SizeBytes: 1024,
                    Sha256: "abc123",
                    CreatedAtUtc: DateTimeOffset.UtcNow),
                new ColdSegmentInfo(
                    Path: "segments/test-service/id-201_id-300.jsonl",
                    FromSortableUniqueId: "id-201",
                    ToSortableUniqueId: "id-300",
                    EventCount: 75,
                    SizeBytes: 2048,
                    Sha256: "def456",
                    CreatedAtUtc: DateTimeOffset.UtcNow),
                new ColdSegmentInfo(
                    Path: "segments/test-service/id-301_id-400.jsonl",
                    FromSortableUniqueId: "id-301",
                    ToSortableUniqueId: "id-400",
                    EventCount: 25,
                    SizeBytes: 512,
                    Sha256: "ghi789",
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        await WriteManifest(manifest);
        var reader = CreateReader();

        // When
        var result = await reader.GetDataRangeSummaryAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.True(result.IsSuccess);
        var summary = result.GetValue();
        Assert.Equal("id-100", summary.OldestSortableUniqueId);
        Assert.Equal("id-400", summary.LatestSortableUniqueId);
        Assert.Equal(150, summary.TotalEventCount);
        Assert.Equal(3, summary.SegmentCount);
        Assert.Equal(3, summary.Segments.Count);
    }

    [Fact]
    public async Task GetDataRangeSummaryAsync_should_return_error_when_manifest_is_corrupted()
    {
        // Given: write invalid JSON to the manifest path
        var manifestPath = ColdStoragePaths.ManifestPath(ServiceId);
        var corruptedData = "{ invalid json }"u8.ToArray();
        await _storage.PutAsync(manifestPath, corruptedData, expectedETag: null, CancellationToken.None);
        var reader = CreateReader();

        // When
        var result = await reader.GetDataRangeSummaryAsync(ServiceId, CancellationToken.None);

        // Then
        Assert.False(result.IsSuccess);
        Assert.IsType<JsonException>(result.GetException());
    }

    [Fact]
    public async Task GetDataRangeSummaryAsync_should_not_expose_internal_segment_details()
    {
        // Given
        var manifest = new ColdManifest(
            ServiceId: ServiceId,
            ManifestVersion: "v1",
            LatestSafeSortableUniqueId: "id-200",
            Segments:
            [
                new ColdSegmentInfo(
                    Path: "segments/test-service/id-100_id-200.jsonl",
                    FromSortableUniqueId: "id-100",
                    ToSortableUniqueId: "id-200",
                    EventCount: 50,
                    SizeBytes: 9999,
                    Sha256: "secret-hash",
                    CreatedAtUtc: DateTimeOffset.Parse("2026-01-01T00:00:00Z"))
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        await WriteManifest(manifest);
        var reader = CreateReader();

        // When
        var result = await reader.GetDataRangeSummaryAsync(ServiceId, CancellationToken.None);

        // Then: ColdSegmentSummary does not contain SizeBytes, Sha256, or CreatedAtUtc
        Assert.True(result.IsSuccess);
        var seg = result.GetValue().Segments[0];
        Assert.Equal("id-100", seg.FromSortableUniqueId);
        Assert.Equal("id-200", seg.ToSortableUniqueId);
        Assert.Equal(50, seg.EventCount);
    }

    private async Task WriteManifest(ColdManifest manifest)
    {
        var manifestPath = ColdStoragePaths.ManifestPath(manifest.ServiceId);
        var data = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        await _storage.PutAsync(manifestPath, data, expectedETag: null, CancellationToken.None);
    }
}
