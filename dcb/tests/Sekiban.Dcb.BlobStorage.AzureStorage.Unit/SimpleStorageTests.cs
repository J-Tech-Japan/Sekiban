using System.Text;
using Azure.Storage.Blobs;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.BlobStorage.AzureStorage.Unit;

/// <summary>
/// Simple storage tests using Docker-based Azurite
/// </summary>
[Collection("AzuriteCollection")]
public class SimpleStorageTests
{
    private readonly AzuriteTestFixture _fixture;

    public SimpleStorageTests(AzuriteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AzureBlobAccessor_Can_Write_And_Read()
    {
        // Use the connection string from the fixture
        var container = "snapshots-unit";
        var accessor = new AzureBlobStorageSnapshotAccessor(_fixture.ConnectionString, container);
        var projector = "unit-projector";

        var bytes = Encoding.UTF8.GetBytes("hello world");
        using var writeStream = new MemoryStream(bytes);
        var key = await accessor.WriteAsync(writeStream, projector);

        Assert.False(string.IsNullOrWhiteSpace(key));
        await using var readStream = await accessor.OpenReadAsync(key);
        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        var roundtrip = ms.ToArray();
        Assert.Equal(bytes, roundtrip);
    }

    [Fact]
    public async Task Actor_Snapshot_Is_Inline()
    {
        // Arrange domain
        var domain = CreateDomain();
        var actor = new GeneralMultiProjectionActor(
            domain,
            BigPayloadProjector.MultiProjectorName,
            new GeneralMultiProjectionActorOptions { SafeWindowMs = 1000 });

        // Add a few large events
        foreach (var i in Enumerable.Range(0, 10))
        {
            var payload = new Created(new string('x', 5000)); // larger payload
            await actor.AddEventsAsync(new[] { MakeEvent(payload) });
        }

        // Act
        var snapshot = await actor.GetSnapshotAsync(true);
        Assert.True(snapshot.IsSuccess);
        var env = snapshot.GetValue();

        // Assert - inline
        Assert.False(env.IsOffloaded);
        Assert.NotNull(env.InlineState);
    }

    private static Event MakeEvent(IEventPayload payload)
    {
        var sortableId = SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid());
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Test"),
            new List<string>());
    }

    private static DcbDomainTypes CreateDomain()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<Created>("Created");
        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<BigPayloadProjector>();
        return new DcbDomainTypes(eventTypes, tagTypes, tagProjectorTypes, tagStatePayloadTypes, multiProjectorTypes, new SimpleQueryTypes());
    }

    public record Created(string Text) : IEventPayload;

    public record BigPayloadProjector(List<string> Items) : IMultiProjector<BigPayloadProjector>
    {
        public BigPayloadProjector() : this(new List<string>()) { }
        public static string MultiProjectorVersion => "1.0";
        public static string MultiProjectorName => "BigPayloadProjector";
        public static BigPayloadProjector GenerateInitialPayload() => new(new List<string>());
        public static ResultBox<BigPayloadProjector> Project(
            BigPayloadProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ev.Payload switch
            {
                Created c => ResultBox.FromValue(payload with { Items = payload.Items.Concat(new[] { c.Text }).ToList() }),
                _ => ResultBox.FromValue(payload)
            };
    }
}
