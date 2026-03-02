using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Tags;
using CoreInMemoryEventStore = Sekiban.Dcb.InMemory.InMemoryEventStore;
using CoreInMemoryMultiProjectionStateStore = Sekiban.Dcb.InMemory.InMemoryMultiProjectionStateStore;

namespace Sekiban.Dcb.Tests;

public class ServiceIdIsolationTests
{
    [Fact]
    public async Task InMemoryEventStore_Isolates_By_ServiceId()
    {
        var provider = new MutableServiceIdProvider("alpha");
        var store = new CoreInMemoryEventStore(provider);

        var studentId = Guid.NewGuid();
        var evt = EventTestHelper.CreateEvent(new StudentCreated(studentId, "Alice"), new StudentTag(studentId));
        var writeResult = await store.WriteEventsAsync([evt]);
        Assert.True(writeResult.IsSuccess);

        var readAlpha = await store.ReadAllEventsAsync();
        Assert.True(readAlpha.IsSuccess);
        Assert.Single(readAlpha.GetValue());

        provider.ServiceId = "beta";
        var readBeta = await store.ReadAllEventsAsync();
        Assert.True(readBeta.IsSuccess);
        Assert.Empty(readBeta.GetValue());

        var readEventBeta = await store.ReadEventAsync(evt.Id);
        Assert.False(readEventBeta.IsSuccess);

        provider.ServiceId = "alpha";
        var readEventAlpha = await store.ReadEventAsync(evt.Id);
        Assert.True(readEventAlpha.IsSuccess);
    }

    [Fact]
    public async Task InMemoryMultiProjectionStateStore_Isolates_By_ServiceId()
    {
        var provider = new MutableServiceIdProvider("alpha");
        var store = new CoreInMemoryMultiProjectionStateStore(provider);

        var request = CreateWriteRequest("Projector", "v1", 10);
        await using var stream = new MemoryStream([1, 2, 3]);
        var upsert = await store.UpsertFromStreamAsync(request, stream, 1_000_000);
        Assert.True(upsert.IsSuccess);

        var alphaLatest = await store.GetLatestAnyVersionAsync("Projector");
        Assert.True(alphaLatest.IsSuccess);
        Assert.True(alphaLatest.GetValue().HasValue);

        provider.ServiceId = "beta";
        var betaLatest = await store.GetLatestAnyVersionAsync("Projector");
        Assert.True(betaLatest.IsSuccess);
        Assert.False(betaLatest.GetValue().HasValue);

        provider.ServiceId = "alpha";
        var alphaLatestAgain = await store.GetLatestAnyVersionAsync("Projector");
        Assert.True(alphaLatestAgain.IsSuccess);
        Assert.True(alphaLatestAgain.GetValue().HasValue);
    }

    private static MultiProjectionStateWriteRequest CreateWriteRequest(string projectorName, string projectorVersion, long eventsProcessed)
    {
        return new MultiProjectionStateWriteRequest(
            ProjectorName: projectorName,
            ProjectorVersion: projectorVersion,
            PayloadType: "payload",
            LastSortableUniqueId: SortableUniqueId.GenerateNew(),
            EventsProcessed: eventsProcessed,
            IsOffloaded: false,
            OffloadKey: null,
            OffloadProvider: null,
            OriginalSizeBytes: 0,
            CompressedSizeBytes: 0,
            SafeWindowThreshold: string.Empty,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            BuildSource: "tests",
            BuildHost: null);
    }

    private sealed class MutableServiceIdProvider : IServiceIdProvider
    {
        private string _serviceId;

        public MutableServiceIdProvider(string serviceId)
        {
            _serviceId = serviceId;
        }

        public string ServiceId
        {
            get => _serviceId;
            set => _serviceId = value;
        }

        public string GetCurrentServiceId() => ServiceIdValidator.NormalizeAndValidate(_serviceId);
    }
}
