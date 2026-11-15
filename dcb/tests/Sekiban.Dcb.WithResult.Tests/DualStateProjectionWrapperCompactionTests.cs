using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using System.Reflection;

namespace Sekiban.Dcb.Tests;

public class DualStateProjectionWrapperCompactionTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly SimpleMultiProjectorTypes _multiProjectorTypes;

    public DualStateProjectionWrapperCompactionTests()
    {
        var eventTypes = new SimpleEventTypes();
        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();

        _multiProjectorTypes = new SimpleMultiProjectorTypes();
        _multiProjectorTypes.RegisterProjector<CountingProjector>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            _multiProjectorTypes,
            new SimpleQueryTypes(),
            new System.Text.Json.JsonSerializerOptions());
    }

    [Fact]
    public void CompactSafeHistory_ShouldDropDuplicateTrackingForPromotedEvents()
    {
        var wrapper = CreateWrapper();
        var oldThreshold = new SortableUniqueId("000000000000000000000000000000000000000000000000");
        var now = DateTime.UtcNow;

        var event1 = CreateEvent(new CountedEvent(1), now);
        var event2 = CreateEvent(new CountedEvent(2), now.AddSeconds(1));

        wrapper.ProcessEvent(event1, oldThreshold, _domainTypes);
        wrapper.ProcessEvent(event2, oldThreshold, _domainTypes);

        Assert.Equal(2, GetPrivateCount(wrapper, "_bufferedEvents"));
        Assert.Equal(2, GetPrivateCount(wrapper, "_processedEventIds"));

        var futureThreshold = SortableUniqueId.Generate(now.AddMinutes(5), Guid.Empty);
        var safeProjection = wrapper.GetSafeProjection(futureThreshold, _domainTypes);

        Assert.Equal(3, safeProjection.State.Total);
        Assert.Equal(0, GetPrivateCount(wrapper, "_bufferedEvents"));
        Assert.Equal(2, GetPrivateCount(wrapper, "_processedEventIds"));

        ((IDualStateAccessor)wrapper).CompactSafeHistory();

        Assert.Equal(0, GetPrivateCount(wrapper, "_processedEventIds"));
    }

    [Fact]
    public void CompactSafeHistory_ShouldKeepOnlyBufferedDuplicateTracking()
    {
        var wrapper = CreateWrapper();
        var oldThreshold = new SortableUniqueId("000000000000000000000000000000000000000000000000");
        var now = DateTime.UtcNow;

        var safeEvent = CreateEvent(new CountedEvent(1), now);
        var bufferedEvent = CreateEvent(new CountedEvent(2), now.AddSeconds(1));

        var futureThreshold = SortableUniqueId.Generate(now.AddMinutes(5), Guid.Empty);
        wrapper.ProcessEvent(safeEvent, futureThreshold, _domainTypes);
        wrapper.ProcessEvent(bufferedEvent, oldThreshold, _domainTypes);

        Assert.Equal(1, GetPrivateCount(wrapper, "_allSafeEvents"));
        Assert.Equal(1, GetPrivateCount(wrapper, "_bufferedEvents"));
        Assert.Equal(2, GetPrivateCount(wrapper, "_processedEventIds"));

        ((IDualStateAccessor)wrapper).CompactSafeHistory();

        Assert.Equal(0, GetPrivateCount(wrapper, "_allSafeEvents"));
        Assert.Equal(1, GetPrivateCount(wrapper, "_bufferedEvents"));
        Assert.Equal(1, GetPrivateCount(wrapper, "_processedEventIds"));
        var safePayload = Assert.IsType<CountingProjector>(((IDualStateAccessor)wrapper).GetSafeProjectorPayload());
        Assert.Equal(1, safePayload.Total);

        var versionBeforeDuplicate = ((IDualStateAccessor)wrapper).UnsafeVersion;
        wrapper.ProcessEvent(bufferedEvent, oldThreshold, _domainTypes);

        Assert.Equal(versionBeforeDuplicate, ((IDualStateAccessor)wrapper).UnsafeVersion);
    }

    [Fact]
    public void CompactSafeHistory_ShouldNotThrow_WhenSafeAndBufferedHistoryAreEmpty()
    {
        var wrapper = CreateWrapper();

        var exception = Record.Exception(() => ((IDualStateAccessor)wrapper).CompactSafeHistory());

        Assert.Null(exception);
        Assert.Equal(0, GetPrivateCount(wrapper, "_allSafeEvents"));
        Assert.Equal(0, GetPrivateCount(wrapper, "_bufferedEvents"));
        Assert.Equal(0, GetPrivateCount(wrapper, "_processedEventIds"));
    }

    [Fact]
    public void CompactSafeHistory_ShouldClearSortedSafeHistory_WhenSafeHistoryHadEntries()
    {
        var wrapper = CreateWrapper();
        var futureThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(5), Guid.Empty);
        var now = DateTime.UtcNow;

        for (var i = 0; i < 32; i++)
        {
            wrapper.ProcessEvent(CreateEvent(new CountedEvent(1), now.AddSeconds(i)), futureThreshold, _domainTypes);
        }

        Assert.Equal(32, GetPrivateCount(wrapper, "_allSafeEvents"));

        ((IDualStateAccessor)wrapper).CompactSafeHistory();

        Assert.Equal(0, GetPrivateCount(wrapper, "_allSafeEvents"));
    }

    [Theory]
    [InlineData("restore")]
    [InlineData("compact-before")]
    [InlineData("compact-after")]
    [InlineData("fresh")]
    public void AlreadySafeArrival_PromotesOlderBufferInOrder(string mode)
    {
        var now = DateTime.UtcNow;
        var zero = new SortableUniqueId(new string('0', 48));
        var future = SortableUniqueId.Generate(now.AddMinutes(1), Guid.Empty);
        var wrapper = mode == "restore"
            ? new DualStateProjectionWrapper<CountingProjector>(new CountingProjector(),
                CountingProjector.MultiProjectorName, _multiProjectorTypes, _domainTypes.JsonSerializerOptions,
                initialVersion: 1, initialLastEventId: Guid.NewGuid(),
                initialLastSortableUniqueId: SortableUniqueId.Generate(now.AddMinutes(-1), Guid.Empty))
            : CreateWrapper();
        if (mode == "compact-before") ((IDualStateAccessor)wrapper).CompactSafeHistory();
        var b = CreateEvent(new CountedEvent(2), now);
        var c = CreateEvent(new CountedEvent(3), now.AddSeconds(1));
        wrapper.ProcessEvent(b, zero, _domainTypes);
        if (mode == "compact-after") ((IDualStateAccessor)wrapper).CompactSafeHistory();
        wrapper.ProcessEvent(c, future, _domainTypes);
        var safe = wrapper.GetSafeProjection(future, _domainTypes);
        Assert.False(ReadSignal<bool>(wrapper, "RebuildRequired"));
        Assert.Equal("23", safe.State.Order);
        Assert.Equal(5, safe.State.Total);
        Assert.Equal("23", wrapper.GetUnsafeProjection(_domainTypes).State.Order);
        Assert.Equal(0, GetPrivateCount(wrapper, "_bufferedEvents"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenuineLateEvent_PreservesSignalTimeSafeHead(bool promotion)
    {
        var now = DateTime.UtcNow;
        var zero = new SortableUniqueId(new string('0', 48));
        var future = SortableUniqueId.Generate(now.AddMinutes(1), Guid.Empty);
        var wrapper = CreateWrapper();
        var head = CreateEvent(new CountedEvent(3), now);
        wrapper.ProcessEvent(head, future, _domainTypes);
        ((IDualStateAccessor)wrapper).CompactSafeHistory();
        var late = CreateEvent(new CountedEvent(2), now.AddSeconds(-1));
        var before = DateTime.UtcNow;
        wrapper.ProcessEvent(late, promotion ? zero : future, _domainTypes);
        if (promotion) ((IDualStateAccessor)wrapper).PromoteBufferedEvents(future, _domainTypes);
        Assert.True(ReadSignal<bool>(wrapper, "RebuildRequired"));
        Assert.Equal(late.Id.ToString(), ReadSignal<string?>(wrapper, "RebuildOffendingEventId"));
        Assert.Equal(late.SortableUniqueIdValue, ReadSignal<string?>(wrapper, "RebuildOffendingPosition"));
        Assert.Equal(head.Id.ToString(), ReadSignal<string?>(wrapper, "RebuildSafeHeadEventId"));
        Assert.Equal(head.SortableUniqueIdValue, ReadSignal<string?>(wrapper, "RebuildSafeHeadPosition"));
        Assert.Equal(promotion ? "promotion behind safe head" : "safe arrival behind safe head", ReadSignal<string?>(wrapper, "RebuildReason"));
        Assert.InRange(ReadSignal<DateTime?>(wrapper, "RebuildSignaledAtUtc")!.Value, before, DateTime.UtcNow);
    }

    private DualStateProjectionWrapper<CountingProjector> CreateWrapper() =>
        new(
            CountingProjector.GenerateInitialPayload(),
            CountingProjector.MultiProjectorName,
            _multiProjectorTypes,
            _domainTypes.JsonSerializerOptions);

    private static Event CreateEvent(CountedEvent payload, DateTime timestamp)
    {
        var eventId = Guid.NewGuid();
        return new Event(
            payload,
            SortableUniqueId.Generate(timestamp, eventId),
            typeof(CountedEvent).Name,
            eventId,
            new EventMetadata(eventId.ToString(), eventId.ToString(), "TestUser"),
            new List<string>());
    }

    private static T ReadSignal<T>(object wrapper, string property)
    {
        var contract = wrapper.GetType().GetInterfaces().Single(type => type.Name == "IDualStateRebuildSignals");
        return (T)contract.GetProperty(property)!.GetValue(wrapper)!;
    }

    private static int GetPrivateCount(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var value = field!.GetValue(target);
        Assert.NotNull(value);

        var countProperty = value!.GetType().GetProperty("Count");
        Assert.NotNull(countProperty);

        return (int)countProperty!.GetValue(value)!;
    }

    public record CountedEvent(int Amount) : IEventPayload;

    public record CountingProjector : IMultiProjector<CountingProjector>
    {
        public int Total { get; init; }
        public string Order { get; init; } = string.Empty;

        public static string MultiProjectorName => "CountingProjector";
        public static string MultiProjectorVersion => "1.0.0";
        public static CountingProjector GenerateInitialPayload() => new();

        public static ResultBox<CountingProjector> Project(
            CountingProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold)
        {
            return ev.Payload is CountedEvent counted
                ? ResultBox.FromValue(payload with { Total = payload.Total + counted.Amount, Order = payload.Order + counted.Amount })
                : ResultBox.FromValue(payload);
        }
    }
}
