using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Unit tests for MultiProjectionStateWriteRequest record type.
///     Verifies construction and ToRecord() conversion to MultiProjectionStateRecord.
/// </summary>
public class MultiProjectionStateWriteRequestTests
{
    private static MultiProjectionStateWriteRequest CreateSampleRequest(
        byte[]? stateData = null,
        bool isOffloaded = false,
        string? offloadKey = null,
        string? offloadProvider = null) =>
        new(
            ProjectorName: "TestProjector",
            ProjectorVersion: "1.0",
            PayloadType: "TestPayloadType",
            LastSortableUniqueId: "20260225T120000Z-00000000-0000-0000-0000-000000000001",
            EventsProcessed: 42,
            StateData: stateData,
            IsOffloaded: isOffloaded,
            OffloadKey: offloadKey,
            OffloadProvider: offloadProvider,
            OriginalSizeBytes: 1024,
            CompressedSizeBytes: 512,
            SafeWindowThreshold: "20260225T115940Z-00000000-0000-0000-0000-000000000000",
            CreatedAt: new DateTime(2026, 2, 25, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt: new DateTime(2026, 2, 25, 12, 0, 0, DateTimeKind.Utc),
            BuildSource: "UnitTest",
            BuildHost: "test-host");

    [Fact]
    public void Construction_Should_Preserve_All_Fields()
    {
        // Given/When
        var request = CreateSampleRequest(
            stateData: new byte[] { 1, 2, 3 },
            isOffloaded: false);

        // Then
        Assert.Equal("TestProjector", request.ProjectorName);
        Assert.Equal("1.0", request.ProjectorVersion);
        Assert.Equal("TestPayloadType", request.PayloadType);
        Assert.Equal(42, request.EventsProcessed);
        Assert.NotNull(request.StateData);
        Assert.Equal(new byte[] { 1, 2, 3 }, request.StateData);
        Assert.False(request.IsOffloaded);
        Assert.Null(request.OffloadKey);
        Assert.Null(request.OffloadProvider);
        Assert.Equal(1024, request.OriginalSizeBytes);
        Assert.Equal(512, request.CompressedSizeBytes);
        Assert.Equal("UnitTest", request.BuildSource);
        Assert.Equal("test-host", request.BuildHost);
    }

    [Fact]
    public void Construction_Should_Accept_Null_StateData_For_Offloaded_Case()
    {
        // Given/When
        var request = CreateSampleRequest(
            stateData: null,
            isOffloaded: true,
            offloadKey: "blob/key/abc",
            offloadProvider: "AzureBlobStorage");

        // Then
        Assert.Null(request.StateData);
        Assert.True(request.IsOffloaded);
        Assert.Equal("blob/key/abc", request.OffloadKey);
        Assert.Equal("AzureBlobStorage", request.OffloadProvider);
    }

    [Fact]
    public void ToRecord_Should_Convert_Inline_Request_To_MultiProjectionStateRecord()
    {
        // Given
        var inlineData = new byte[] { 10, 20, 30, 40 };
        var request = CreateSampleRequest(stateData: inlineData);

        // When
        var record = request.ToRecord();

        // Then: all metadata is preserved
        Assert.Equal(request.ProjectorName, record.ProjectorName);
        Assert.Equal(request.ProjectorVersion, record.ProjectorVersion);
        Assert.Equal(request.PayloadType, record.PayloadType);
        Assert.Equal(request.LastSortableUniqueId, record.LastSortableUniqueId);
        Assert.Equal(request.EventsProcessed, record.EventsProcessed);
        Assert.Equal(inlineData, record.StateData);
        Assert.Equal(request.IsOffloaded, record.IsOffloaded);
        Assert.Equal(request.OffloadKey, record.OffloadKey);
        Assert.Equal(request.OffloadProvider, record.OffloadProvider);
        Assert.Equal(request.OriginalSizeBytes, record.OriginalSizeBytes);
        Assert.Equal(request.CompressedSizeBytes, record.CompressedSizeBytes);
        Assert.Equal(request.SafeWindowThreshold, record.SafeWindowThreshold);
        Assert.Equal(request.CreatedAt, record.CreatedAt);
        Assert.Equal(request.UpdatedAt, record.UpdatedAt);
        Assert.Equal(request.BuildSource, record.BuildSource);
        Assert.Equal(request.BuildHost, record.BuildHost);
    }

    [Fact]
    public void ToRecord_Should_Convert_Offloaded_Request_With_Null_StateData()
    {
        // Given
        var request = CreateSampleRequest(
            stateData: null,
            isOffloaded: true,
            offloadKey: "projector/v1/abc123",
            offloadProvider: "InMemoryBlobStorage");

        // When
        var record = request.ToRecord();

        // Then: offload metadata preserved, StateData is null
        Assert.Null(record.StateData);
        Assert.True(record.IsOffloaded);
        Assert.Equal("projector/v1/abc123", record.OffloadKey);
        Assert.Equal("InMemoryBlobStorage", record.OffloadProvider);
    }

    [Fact]
    public void ToRecord_Should_Produce_Correct_PartitionKey()
    {
        // Given
        var request = CreateSampleRequest();

        // When
        var record = request.ToRecord();

        // Then
        Assert.Equal("MultiProjectionState_TestProjector", record.GetPartitionKey());
    }

    [Fact]
    public void ToRecord_Should_Produce_Correct_DocumentId()
    {
        // Given
        var request = CreateSampleRequest();

        // When
        var record = request.ToRecord();

        // Then
        Assert.Equal("1.0", record.GetDocumentId());
    }
}
