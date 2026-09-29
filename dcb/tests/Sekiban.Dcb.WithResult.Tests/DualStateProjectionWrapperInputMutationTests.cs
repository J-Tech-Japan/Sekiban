using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Sekiban.Dcb.Tests;

/// <summary>Regression coverage for #1253 item 1.</summary>
public class DualStateProjectionWrapperInputMutationTests
{
    [Fact]
    public void MarkerPayload_IsolatesSafeStateDuringOutOfOrderUnsafeReconcile()
    {
        var (types, domain) = CreateDomain<MarkedMutatingCounter>();
        var wrapper = new DualStateProjectionWrapper<MarkedMutatingCounter>(
            MarkedMutatingCounter.GenerateInitialPayload(),
            MarkedMutatingCounter.MultiProjectorName,
            types,
            domain.JsonSerializerOptions);

        ApplyReportedTrace(wrapper, domain);

        Assert.Equal(0, wrapper.GetSafeProjection(Threshold(), domain).State.Total);
        Assert.Equal(3, wrapper.GetUnsafeProjection(domain).State.Total);
        Assert.NotSame(GetField(wrapper, "_safeProjector"), GetField(wrapper, "_unsafeProjector"));
        Assert.True(MarkedMutatingCounter.SerializeCount > 0);
    }

    [Fact]
    public void RuntimeMarkerPayload_IsolatedWhenWrapperGenericTypeIsInterface()
    {
        var (types, domain) = CreateDomain<MarkedMutatingCounter>();
        var wrapper = new DualStateProjectionWrapper<IMultiProjectionPayload>(
            MarkedMutatingCounter.GenerateInitialPayload(),
            MarkedMutatingCounter.MultiProjectorName,
            types,
            domain.JsonSerializerOptions);

        ApplyReportedTrace(wrapper, domain);

        Assert.Equal(0, Assert.IsType<MarkedMutatingCounter>(
            wrapper.GetSafeProjection(Threshold(), domain).State).Total);
        Assert.Equal(3, Assert.IsType<MarkedMutatingCounter>(
            wrapper.GetUnsafeProjection(domain).State).Total);
    }

    [Fact]
    public void EmptyBufferMarkerExposure_NeverReturnsSafeInstanceAsServed()
    {
        var (types, domain) = CreateDomain<MarkedMutatingCounter>();
        var wrapper = new DualStateProjectionWrapper<MarkedMutatingCounter>(
            MarkedMutatingCounter.GenerateInitialPayload(),
            MarkedMutatingCounter.MultiProjectorName,
            types,
            domain.JsonSerializerOptions);
        var zero = ReportedEvents().Safe;

        wrapper.ProcessEvent(zero, Threshold(), domain);

        var safe = wrapper.GetSafeProjection(Threshold(), domain).State;
        var served = wrapper.GetUnsafeProjection(domain).State;
        Assert.NotSame(safe, served);

        var accessorWrapper = new DualStateProjectionWrapper<MarkedMutatingCounter>(
            MarkedMutatingCounter.GenerateInitialPayload(),
            MarkedMutatingCounter.MultiProjectorName,
            types,
            domain.JsonSerializerOptions);
        accessorWrapper.ProcessEvent(zero, Threshold(), domain);
        var accessor = (IDualStateAccessor)accessorWrapper;
        accessor.PromoteBufferedEvents(Threshold(), domain);
        Assert.NotSame(accessor.GetSafeProjectorPayload(), accessor.GetUnsafeProjectorPayload());
    }

    [Fact]
    public void FreshMarker_FirstUnsafeFoldClonesBaselineThroughRegisteredSerializer()
    {
        var (types, domain) = CreateDomain<MarkedMutatingCounter>();
        var initial = new MarkedMutatingCounter { Values = new() { ["total"] = 7 } };
        var wrapper = new DualStateProjectionWrapper<MarkedMutatingCounter>(
            initial,
            MarkedMutatingCounter.MultiProjectorName,
            types,
            domain.JsonSerializerOptions);

        Assert.Equal(0, MarkedMutatingCounter.SerializeCount);

        wrapper.ProcessEvent(CreateEvent(2, DateTime.UtcNow, 20), Threshold(), domain);

        Assert.Equal(7, wrapper.GetSafeProjection(Threshold(), domain).State.Total);
        Assert.Equal(9, wrapper.GetUnsafeProjection(domain).State.Total);
        Assert.Equal(1, MarkedMutatingCounter.SerializeCount);
    }

    [Fact]
    public void UnmarkedPayload_DocumentsInputMutationContractViolation()
    {
        var (types, domain) = CreateDomain<UnmarkedMutatingCounter>();
        var wrapper = new DualStateProjectionWrapper<UnmarkedMutatingCounter>(
            UnmarkedMutatingCounter.GenerateInitialPayload(),
            UnmarkedMutatingCounter.MultiProjectorName,
            types,
            domain.JsonSerializerOptions);

        ApplyReportedTrace(wrapper, domain);

        Assert.Equal(5, wrapper.GetSafeProjection(Threshold(), domain).State.Total);
        Assert.Equal(5, wrapper.GetUnsafeProjection(domain).State.Total);
        Assert.Equal(0, UnmarkedMutatingCounter.SerializeCount);
    }

    [Fact]
    public async Task Actor_MarkerPayload_ProducesUnsafeThreeAndSafeZero()
    {
        var (_, domain) = CreateDomain<MarkedMutatingCounter>();
        var actor = new GeneralMultiProjectionActor(
            domain,
            MarkedMutatingCounter.MultiProjectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = 60_000,
                VerifySafeStateIsolation = true
            });

        await ApplyReportedTrace(actor);

        var safe = await actor.GetStateAsync(canGetUnsafeState: false);
        var served = await actor.GetStateAsync(canGetUnsafeState: true);
        Assert.Equal(0, Assert.IsType<MarkedMutatingCounter>(safe.GetValue().Payload).Total);
        Assert.Equal(3, Assert.IsType<MarkedMutatingCounter>(served.GetValue().Payload).Total);
    }

    [Fact]
    public async Task Actor_VerificationFailsFastAtAddEventsAsyncForUnmarkedMutation()
    {
        var (_, domain) = CreateDomain<UnmarkedMutatingCounter>();
        var actor = new GeneralMultiProjectionActor(
            domain,
            UnmarkedMutatingCounter.MultiProjectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = 60_000,
                VerifySafeStateIsolation = true
            });
        var events = ReportedEvents();

        await actor.AddEventsAsync([events.Safe]);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => actor.AddEventsAsync([events.LaterUnsafe]));

        Assert.StartsWith("Multi-projector mutated its input payload:", failure.Message);
        Assert.Contains(nameof(IMutatesProjectionInput), failure.Message);
    }

    [Fact]
    public async Task Actor_VerificationOffAddsNoSerializationForUnmarkedPayload()
    {
        var (_, domain) = CreateDomain<UnmarkedMutatingCounter>();
        var actor = new GeneralMultiProjectionActor(
            domain,
            UnmarkedMutatingCounter.MultiProjectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = 60_000,
                VerifySafeStateIsolation = false
            });

        await ApplyReportedTrace(actor);

        Assert.Equal(0, UnmarkedMutatingCounter.SerializeCount);
    }

    [Fact]
    public async Task Actor_VerificationAllowsPureProjector()
    {
        var (_, domain) = CreateDomain<PureCounter>();
        var actor = new GeneralMultiProjectionActor(
            domain,
            PureCounter.MultiProjectorName,
            new GeneralMultiProjectionActorOptions
            {
                SafeWindowMs = 60_000,
                VerifySafeStateIsolation = true
            });

        await ApplyReportedTrace(actor);

        var safe = await actor.GetStateAsync(canGetUnsafeState: false);
        var served = await actor.GetStateAsync(canGetUnsafeState: true);
        Assert.Equal(0, Assert.IsType<PureCounter>(safe.GetValue().Payload).Total);
        Assert.Equal(3, Assert.IsType<PureCounter>(served.GetValue().Payload).Total);
    }

    [Fact]
    public void RestoredMarkerPayload_UsesSnapshotCloneAndKeepsBackingFieldsDistinct()
    {
        var (types, domain) = CreateDomain<MarkedMutatingCounter>();
        var restored = new MarkedMutatingCounter { Values = new() { ["total"] = 7 } };

        var wrapper = Assert.IsType<DualStateProjectionWrapper<MarkedMutatingCounter>>(
            DualStateProjectionWrapperFactory.CreateFromRestoredSnapshot(
                restored,
                MarkedMutatingCounter.MultiProjectorName,
                types,
                domain,
                SortableUniqueId.MinValue.Value,
                initialVersion: 4));

        Assert.Equal(1, MarkedMutatingCounter.SerializeCount);
        Assert.NotSame(GetField(wrapper, "_safeProjector"), GetField(wrapper, "_unsafeProjector"));
    }

    [Fact]
    public void StreamingRestore_SameMarkerReferenceIsClonedAndTraceRemainsIsolated()
    {
        var (types, domain) = CreateDomain<MarkedMutatingCounter>();
        var restored = MarkedMutatingCounter.GenerateInitialPayload();
        var streamingRestore = typeof(DualStateProjectionWrapperFactory)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(method =>
                method.Name == nameof(DualStateProjectionWrapperFactory.CreateFromRestoredSnapshot)
                && method.GetParameters().Length == 10);
        var wrapper = Assert.IsType<DualStateProjectionWrapper<MarkedMutatingCounter>>(
            streamingRestore.Invoke(null,
            [
                restored,
                restored,
                MarkedMutatingCounter.MultiProjectorName,
                types,
                domain,
                SortableUniqueId.MinValue.Value,
                0,
                Guid.Empty,
                null,
                false
            ]));

        Assert.NotSame(GetField(wrapper, "_safeProjector"), GetField(wrapper, "_unsafeProjector"));

        ApplyReportedTrace(wrapper, domain);

        Assert.Equal(0, wrapper.GetSafeProjection(Threshold(), domain).State.Total);
        Assert.Equal(3, wrapper.GetUnsafeProjection(domain).State.Total);
    }

    private static object GetField<T>(DualStateProjectionWrapper<T> wrapper, string name)
        where T : IMultiProjectionPayload =>
        typeof(DualStateProjectionWrapper<T>)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(wrapper)!;

    private static void ApplyReportedTrace<T>(DualStateProjectionWrapper<T> wrapper, DcbDomainTypes domain)
        where T : IMultiProjectionPayload
    {
        var events = ReportedEvents();
        wrapper.ProcessEvent(events.Safe, Threshold(), domain);
        wrapper.ProcessEvent(events.LaterUnsafe, Threshold(), domain);
        wrapper.ProcessEvent(events.EarlierUnsafe, Threshold(), domain);
    }

    private static async Task ApplyReportedTrace(GeneralMultiProjectionActor actor)
    {
        var events = ReportedEvents();
        await actor.AddEventsAsync([events.Safe]);
        await actor.AddEventsAsync([events.LaterUnsafe]);
        await actor.AddEventsAsync([events.EarlierUnsafe]);
    }

    private static (Event Safe, Event LaterUnsafe, Event EarlierUnsafe) ReportedEvents()
    {
        var now = DateTime.UtcNow;
        return (
            CreateEvent(0, now.AddMinutes(-2), 1),
            CreateEvent(2, now.AddSeconds(-1), 2),
            CreateEvent(1, now.AddSeconds(-2), 3));
    }

    private static SortableUniqueId Threshold() =>
        new(SortableUniqueId.Generate(DateTime.UtcNow.AddMinutes(-1), Guid.Empty));

    private static Event CreateEvent(int amount, DateTime timestamp, int id) =>
        new(
            new AddAmount(amount),
            SortableUniqueId.Generate(timestamp, Guid.Empty),
            nameof(AddAmount),
            new Guid(id, 0, 0, new byte[8]),
            new EventMetadata("root", "causation", "test"),
            []);

    private static (SimpleMultiProjectorTypes Types, DcbDomainTypes Domain) CreateDomain<T>()
        where T : IMultiProjectorWithCustomSerialization<T>, new()
    {
        ResetSerializationCount<T>();
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<AddAmount>(nameof(AddAmount));
        var types = new SimpleMultiProjectorTypes();
        Assert.True(types.RegisterProjectorWithCustomSerialization<T>().IsSuccess);
        var domain = new DcbDomainTypes(
            eventTypes,
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            types,
            new SimpleQueryTypes());
        return (types, domain);
    }

    private static void ResetSerializationCount<T>()
    {
        if (typeof(T) == typeof(MarkedMutatingCounter)) MarkedMutatingCounter.SerializeCount = 0;
        if (typeof(T) == typeof(UnmarkedMutatingCounter)) UnmarkedMutatingCounter.SerializeCount = 0;
        if (typeof(T) == typeof(PureCounter)) PureCounter.SerializeCount = 0;
    }

    public record AddAmount(int Amount) : IEventPayload;

    public sealed record MarkedMutatingCounter : IMultiProjectorWithCustomSerialization<MarkedMutatingCounter>,
        IMutatesProjectionInput
    {
        [JsonIgnore]
        public Dictionary<string, int> Values { get; init; } = new();
        public int Total => Values.GetValueOrDefault("total");
        public static int SerializeCount { get; set; }
        public static string MultiProjectorName => nameof(MarkedMutatingCounter);
        public static string MultiProjectorVersion => "1";
        public static MarkedMutatingCounter GenerateInitialPayload() => new();
        public static ResultBox<MarkedMutatingCounter> Project(MarkedMutatingCounter payload, Event ev,
            List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold)
        {
            payload.Values["total"] = payload.Total + ((AddAmount)ev.Payload).Amount;
            return ResultBox.FromValue(payload);
        }
        public static SerializationResult Serialize(DcbDomainTypes domainTypes, string threshold,
            MarkedMutatingCounter payload)
        {
            SerializeCount++;
            return Bytes(payload.Total);
        }
        public static MarkedMutatingCounter Deserialize(DcbDomainTypes domainTypes, string threshold,
            ReadOnlySpan<byte> data) => new() { Values = new() { ["total"] = BitConverter.ToInt32(data) } };
    }

    public sealed record UnmarkedMutatingCounter : IMultiProjectorWithCustomSerialization<UnmarkedMutatingCounter>
    {
        public Dictionary<string, int> Values { get; init; } = new();
        public int Total => Values.GetValueOrDefault("total");
        public static int SerializeCount { get; set; }
        public static string MultiProjectorName => nameof(UnmarkedMutatingCounter);
        public static string MultiProjectorVersion => "1";
        public static UnmarkedMutatingCounter GenerateInitialPayload() => new();
        public static ResultBox<UnmarkedMutatingCounter> Project(UnmarkedMutatingCounter payload, Event ev,
            List<ITag> tags, DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold)
        {
            payload.Values["total"] = payload.Total + ((AddAmount)ev.Payload).Amount;
            return ResultBox.FromValue(payload);
        }
        public static SerializationResult Serialize(DcbDomainTypes domainTypes, string threshold,
            UnmarkedMutatingCounter payload)
        {
            SerializeCount++;
            return Bytes(payload.Total);
        }
        public static UnmarkedMutatingCounter Deserialize(DcbDomainTypes domainTypes, string threshold,
            ReadOnlySpan<byte> data) => new() { Values = new() { ["total"] = BitConverter.ToInt32(data) } };
    }

    public sealed record PureCounter : IMultiProjectorWithCustomSerialization<PureCounter>
    {
        public int Total { get; init; }
        public PureCounter() { }
        public PureCounter(int total) => Total = total;
        public static int SerializeCount { get; set; }
        public static string MultiProjectorName => nameof(PureCounter);
        public static string MultiProjectorVersion => "1";
        public static PureCounter GenerateInitialPayload() => new();
        public static ResultBox<PureCounter> Project(PureCounter payload, Event ev, List<ITag> tags,
            DcbDomainTypes domainTypes, SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Total = payload.Total + ((AddAmount)ev.Payload).Amount });
        public static SerializationResult Serialize(DcbDomainTypes domainTypes, string threshold, PureCounter payload)
        {
            SerializeCount++;
            return Bytes(payload.Total);
        }
        public static PureCounter Deserialize(DcbDomainTypes domainTypes, string threshold, ReadOnlySpan<byte> data) =>
            new(BitConverter.ToInt32(data));
    }

    private static SerializationResult Bytes(int value)
    {
        var data = BitConverter.GetBytes(value);
        return new SerializationResult(data, data.Length, data.Length);
    }
}
