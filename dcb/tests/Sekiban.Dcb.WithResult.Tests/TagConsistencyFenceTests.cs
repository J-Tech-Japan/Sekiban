extern alias WithoutResultFacade;

using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TagConsistencyFence;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using WithoutGeneral = WithoutResultFacade::Sekiban.Dcb.Actors.GeneralSekibanExecutor;
using WithoutMemory = WithoutResultFacade::Sekiban.Dcb.InMemory.InMemoryDcbExecutor;
using WithoutTesting = WithoutResultFacade::Sekiban.Dcb.Testing.InMemoryDcbExecutorForTesting;

namespace Sekiban.Dcb.Tests;

public sealed class TagConsistencyFenceTests
{
    private static readonly DcbDomainTypes Domain = DomainType.GetDomainTypes();
    private const string Service = DefaultServiceIdProvider.DefaultServiceId;
    private static TagConsistencyFenceOptions On => new() { Mode = TagConsistencyFenceMode.DeriveFromReservations };
    private static WeatherForecastCreated Payload() => new(Guid.NewGuid(), "Tokyo", new(2026, 10, 2), 20, "fence");
    private sealed record Command : ICommand;
    private sealed record Tag(string Content, bool Consistent = true) : ITag
    {
        public string GetTagGroup() => "Fence";
        public string GetTagContent() => Content;
        public bool IsConsistencyTag() => Consistent;
    }
    private static GeneralSekibanExecutor Executor(RecordingStore store, RecordingAccessor accessor, bool on = true) =>
        new(store, accessor, Domain, on ? On : new TagConsistencyFenceOptions());

    [Theory]
    [InlineData(false, "", TagHeadExpectationKind.NoEnforcement)]
    [InlineData(true, "", TagHeadExpectationKind.AssertEmpty)]
    [InlineData(true, "0001", TagHeadExpectationKind.Exact)]
    public async Task Mapping_UsesExactlyTheReservationInput(bool read, string head, TagHeadExpectationKind kind)
    {
        var store = new RecordingStore();
        var accessor = new RecordingAccessor { Head = head };
        var tag = new Tag("mapping");
        var result = await Executor(store, accessor).ExecuteAsync(new Command(), async (_, ctx) =>
        {
            if (read) Assert.True((await ctx.TagExistsAsync(tag)).IsSuccess);
            return EventOrNone.Event(Payload(), tag, new Tag("index", false));
        });
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        var entry = Assert.Single(store.Specification!.Entries);
        Assert.Equal(((ITag)tag).GetTag(), entry.Tag);
        Assert.Equal(kind, entry.Expectation.Kind);
        Assert.Equal(read ? head : null, Assert.Single(accessor.Inputs));
        Assert.Equal(kind == TagHeadExpectationKind.Exact ? head : null, entry.Expectation.Position);
    }

    [Fact]
    public async Task ExplicitConsistencyVersion_WinsOverRead_AndEqualTagsMergeAcrossEvents()
    {
        var store = new RecordingStore();
        var accessor = new RecordingAccessor { Head = "older" };
        var inner = new Tag("explicit");
        var version = SortableUniqueId.GenerateNew();
        var tag = ConsistencyTag.FromTagWithSortableUniqueId(inner, version);
        var result = await Executor(store, accessor).ExecuteCommandAsync(async ctx =>
        {
            await ctx.TagExistsAsync(inner);
            await ctx.AppendEvent(Payload(), [tag]);
            await ctx.AppendEvent(Payload(), [tag]);
            return EventOrNone.None;
        });
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(TagHeadExpectation.Exact(version), Assert.Single(store.Specification!.Entries).Expectation);
        Assert.Equal(version, Assert.Single(accessor.Inputs));
        Assert.Equal(2, store.WrittenCount);
    }

    [Fact]
    public async Task DifferingVersionsForSameTag_RejectBeforeAnyReservation()
    {
        var store = new RecordingStore();
        var accessor = new RecordingAccessor();
        var inner = new Tag("ambiguous");
        var result = await Executor(store, accessor).ExecuteCommandAsync(async ctx =>
        {
            await ctx.AppendEvent(Payload(), [inner]);
            await ctx.AppendEvent(Payload(), [ConsistencyTag.FromTagWithSortableUniqueId(inner, SortableUniqueId.GenerateNew())]);
            return EventOrNone.None;
        });
        Assert.IsType<TagHeadExpectationValidationException>(result.GetException());
        Assert.Empty(accessor.Inputs);
        Assert.Null(store.Specification);
        Assert.Equal(0, store.LegacyWrites);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NoOpOrNoConsistencyTags_DoesNotProduceASpecification(bool noOp)
    {
        var store = new RecordingStore();
        var result = await Executor(store, new()).ExecuteCommandAsync(_ =>
            Task.FromResult(noOp ? EventOrNone.None : EventOrNone.Event(Payload(), new Tag("index", false))));
        Assert.True(result.IsSuccess);
        Assert.Null(store.Specification);
        Assert.Equal(noOp ? 0 : 1, store.LegacyWrites);
        Assert.Equal(1, store.EpochChecks);
    }

    [Theory]
    [InlineData(true, TagConsistencyFenceMode.Off, false)]
    [InlineData(false, TagConsistencyFenceMode.DeriveFromReservations, true)]
    public async Task PerCommandOverride_WorksInBothDirections(bool globalOn, TagConsistencyFenceMode mode, bool fenced)
    {
        var store = new RecordingStore();
        var result = await Executor(store, new(), globalOn).ExecuteAsync(new Command(),
            (_, _) => Task.FromResult(EventOrNone.Event(Payload(), new Tag("override"))),
            new CommandExecutionOptions { TagConsistencyFence = mode });
        Assert.True(result.IsSuccess);
        Assert.Equal(fenced, store.Specification is not null);
    }

    [Fact]
    public async Task ExplicitSpecification_WinsOverDerivedReservation()
    {
        var store = new RecordingStore();
        var tag = new Tag("precedence");
        var spec = new ExpectedTagPositionSpecification([new(Service, ((ITag)tag).GetTag(), TagHeadExpectation.NoEnforcement())]);
        var result = await Executor(store, new()).ExecuteAsync(new Command(), async (_, ctx) =>
        {
            await ctx.TagExistsAsync(tag);
            return EventOrNone.Event(Payload(), tag);
        }, new CommandExecutionOptions { ExpectedTagPositions = spec });
        Assert.True(result.IsSuccess);
        Assert.Same(spec, store.Specification);
        Assert.Equal(1, store.EpochChecks);
    }

    [Fact]
    public async Task UnsupportedStore_FailsAtConstructionAndPerCallBeforeHandler()
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(Domain.EventTypes);
        Assert.Throws<InvalidOperationException>(() => new CoreGeneralSekibanExecutor(store, new RecordingAccessor(), Domain, On));
        var core = new CoreGeneralSekibanExecutor(store, new RecordingAccessor(), Domain);
        var handled = false;
        var result = await core.ExecuteAsync(new Command(), (_, _) =>
        {
            handled = true;
            return Task.FromResult(EventOrNone.None);
        }, new CommandExecutionOptions { TagConsistencyFence = TagConsistencyFenceMode.DeriveFromReservations });
        Assert.IsType<ConditionNotSupportedException>(result.GetException());
        Assert.False(handled);
    }

    [Fact]
    public void HybridOverUnsupportedHotStore_FailsAtConstruction()
    {
        var hybrid = new HybridEventStore(new Sekiban.Dcb.Testing.InMemoryEventStore(Domain.EventTypes), new UnusedColdStorage(),
            new JsonlColdSegmentFormatHandler(), new DefaultServiceIdProvider(),
            Options.Create(new ColdEventStoreOptions()), NullLogger<HybridEventStore>.Instance);
        Assert.Throws<InvalidOperationException>(() => new CoreGeneralSekibanExecutor(hybrid, new RecordingAccessor(), Domain, On));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingEpoch_StopsBeforeHandler_EvenWithExplicitNoEnforcement(bool explicitSpec)
    {
        var store = new RecordingStore { Enabled = false };
        var accessor = new RecordingAccessor();
        var handled = false;
        var result = await Executor(store, accessor).ExecuteAsync(new Command(), (_, _) =>
        {
            handled = true;
            return Task.FromResult(EventOrNone.None);
        }, new CommandExecutionOptions
        {
            ExpectedTagPositions = explicitSpec ? new ExpectedTagPositionSpecification([]) : null
        });
        Assert.IsType<TagHeadEnforcementNotEnabledException>(result.GetException());
        Assert.False(handled);
        Assert.Empty(accessor.Inputs);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Conflict_CancelsWithoutConfirming_NotifiesBestEffort_AndPreservesException(bool notifyThrows, bool storeThrows)
    {
        var tag = new Tag("conflict");
        var conflict = new ExpectedTagPositionConflictException([new(Service, ((ITag)tag).GetTag(), TagHeadExpectation.AssertEmpty(), "new-head")]);
        var store = new RecordingStore { Failure = conflict, Throws = storeThrows };
        var accessor = new RecordingAccessor { NotifyThrows = notifyThrows };
        var result = await Executor(store, accessor).ExecuteCommandAsync(async ctx =>
        {
            await ctx.TagExistsAsync(tag);
            return EventOrNone.Event(Payload(), tag);
        });
        Assert.Same(conflict, result.GetException());
        Assert.Equal(1, accessor.Cancelled);
        Assert.Equal(0, accessor.Confirmed);
        Assert.Equal(1, accessor.Notified);
    }

    [Fact]
    public async Task LiveCapabilityChange_FailsBeforeHandler()
    {
        var store = new RecordingStore();
        var executor = Executor(store, new());
        store.Supported = false;
        var result = await executor.ExecuteCommandAsync(_ => throw new Exception("handler must not run"));
        Assert.IsType<ConditionNotSupportedException>(result.GetException());
    }

    [Fact]
    public async Task ExplicitPerCommandFenceAndConditionalAppend_RejectBeforeHandler()
    {
        var result = await Executor(new(), new()).ExecuteAsync(new Command(), (_, _) => throw new Exception("handler must not run"),
            new CommandExecutionOptions { ConditionalAppend = new("key"), TagConsistencyFence = TagConsistencyFenceMode.DeriveFromReservations });
        Assert.IsType<TagHeadExpectationValidationException>(result.GetException());
    }

    [Fact]
    public async Task GlobalMode_DoesNotChangeConditionalAppend()
    {
        var store = new RecordingStore { Enabled = false };
        var result = await Executor(store, new()).ExecuteAsync(new Command(),
            (_, _) => Task.FromResult(EventOrNone.Event(Payload(), new Tag("conditional"))),
            new CommandExecutionOptions { ConditionalAppend = new("conditional-key") });
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(1, store.ConditionalWrites);
        Assert.Equal(0, store.EpochChecks);
        Assert.Null(store.Specification);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializedConflict_CancelsAndNotifies_WithoutConfirming(bool storeThrows)
    {
        var conflict = new ExpectedTagPositionConflictException([new(Service, "Fence:serialized", TagHeadExpectation.AssertEmpty(), "new-head")]);
        var store = new RecordingStore { Failure = conflict, Throws = storeThrows };
        var accessor = new RecordingAccessor();
        var result = await Executor(store, accessor).CommitSerializableEventsAsync(Request());
        Assert.Same(conflict, result.GetException());
        Assert.Equal(1, accessor.Cancelled);
        Assert.Equal(0, accessor.Confirmed);
        Assert.Equal(1, accessor.Notified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SerializedV1_MissingEpochStopsBeforeReservation(bool returnFalse)
    {
        var store = new RecordingStore { Enabled = false, ReturnFalse = returnFalse };
        var accessor = new RecordingAccessor();
        var result = await Executor(store, accessor).CommitSerializableEventsAsync(Request());
        Assert.IsType<TagHeadEnforcementNotEnabledException>(result.GetException());
        Assert.Empty(accessor.Inputs);
        Assert.Null(store.Specification);
    }

    private static SerializedCommitRequest Request(string? position = "") => new(
        [new SerializableEventCandidate(JsonSerializer.SerializeToUtf8Bytes(Payload(), Domain.JsonSerializerOptions),
            nameof(WeatherForecastCreated), ["Fence:serialized"])], [new ConsistencyTagEntry("Fence:serialized", position!)]);

    [Theory]
    [InlineData("")]
    [InlineData("0001")]
    public async Task SerializedV1_DerivesFromReservationInputs(string position)
    {
        var store = new RecordingStore();
        var accessor = new RecordingAccessor();
        var result = await Executor(store, accessor).CommitSerializableEventsAsync(Request(position));
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(position == "" ? TagHeadExpectation.AssertEmpty() : TagHeadExpectation.Exact(position),
            Assert.Single(store.Specification!.Entries).Expectation);
        Assert.Equal(position, Assert.Single(accessor.Inputs));
    }

    [Fact]
    public async Task SerializedV1_PreservesNullAndDuplicateRejection_AndNoOp()
    {
        var store = new RecordingStore();
        var accessor = new RecordingAccessor();
        var executor = Executor(store, accessor);
        Assert.IsType<ArgumentException>((await executor.CommitSerializableEventsAsync(Request(null))).GetException());
        var req = Request();
        Assert.IsType<InvalidOperationException>((await executor.CommitSerializableEventsAsync(req with
        { ConsistencyTags = [req.ConsistencyTags[0], req.ConsistencyTags[0]] })).GetException());
        Assert.True((await executor.CommitSerializableEventsAsync(new([], []))).IsSuccess);
        Assert.Empty(accessor.Inputs);
        Assert.Null(store.Specification);
    }

    [Fact]
    public async Task SerializedV2_ExplicitWins_AndGlobalModeRequiresEpochBeforeReservation()
    {
        var req = Request();
        var entries = new TagHeadExpectationEntry[] { new(Service, "Fence:serialized", TagHeadExpectation.NoEnforcement()) };
        var request = new VersionedExpectedTagPositionSerializedCommitRequest(2, req.EventCandidates, req.ConsistencyTags, entries);
        var store = new RecordingStore();
        var accessor = new RecordingAccessor();
        var executor = Executor(store, accessor);
        Assert.True((await executor.CommitSerializableEventsWithExpectedTagPositionsAsync(request)).IsSuccess);
        Assert.Equal(TagHeadExpectation.NoEnforcement(), Assert.Single(store.Specification!.Entries).Expectation);
        store.Enabled = false;
        accessor.Inputs.Clear();
        Assert.IsType<TagHeadEnforcementNotEnabledException>((await executor.CommitSerializableEventsWithExpectedTagPositionsAsync(request)).GetException());
        Assert.Empty(accessor.Inputs);
    }

    private sealed record BuiltInCommand : ICommandWithHandler<BuiltInCommand>
    {
        public static Task<ResultBox<EventOrNone>> HandleAsync(BuiltInCommand command, ICommandContext ctx) =>
            Task.FromResult(EventOrNone.Event(Payload(), new Tag("built-in")));
    }

    [Fact]
    public async Task LegacyBuiltInHandlerOverload_UsesGlobalMode()
    {
        var store = new RecordingStore();
        Assert.True((await Executor(store, new()).ExecuteAsync(new BuiltInCommand())).IsSuccess);
        Assert.NotNull(store.Specification);
    }

    public static IEnumerable<object[]> DiCases()
    {
        foreach (var type in new[] { typeof(GeneralSekibanExecutor), typeof(Sekiban.Dcb.InMemory.InMemoryDcbExecutor),
                     typeof(InMemoryDcbExecutorForTesting), typeof(WithoutGeneral), typeof(WithoutMemory), typeof(WithoutTesting), typeof(CoreGeneralSekibanExecutor) })
        foreach (var gate in new[] { false, true })
        foreach (var fence in new[] { false, true })
        foreach (var allocator in new[] { false, true })
        foreach (var optionalServices in new[] { false, true })
            yield return [type, gate, fence, allocator, optionalServices];
    }

    [Theory]
    [MemberData(nameof(DiCases))]
    public void DiResolution_HonorsFenceAndGate_WithAndWithoutAllocatorServices(Type type, bool gate, bool fence, bool allocator, bool optionalServices)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Domain);
        services.AddSingleton<IEventStore>(new Sekiban.Dcb.Testing.InMemoryEventStore(Domain.EventTypes));
        services.AddSingleton<IActorObjectAccessor>(new RecordingAccessor());
        if (optionalServices)
        {
            services.AddSingleton<IEventPublisher, NoPublisher>();
            services.AddSingleton<IExecutedUserProvider, NoUser>();
        }
        if (allocator)
        {
            services.AddSekibanDcbSortableUniqueIdGenerator();
            services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        }
        if (gate) services.AddSekibanDcbExecutorSizeGate(_ => { });
        if (fence) services.AddSekibanDcbTagConsistencyFence(o => o.Mode = TagConsistencyFenceMode.DeriveFromReservations);
        services.AddTransient(type);
        using var provider = services.BuildServiceProvider();
        if (fence)
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService(type));
        else
            Assert.NotNull(provider.GetRequiredService(type));
    }

    private sealed class NoPublisher : IEventPublisher
    {
        public Task PublishAsync(IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class NoUser : IExecutedUserProvider { public string GetExecutedUser() => "test"; }

    private sealed class RecordingAccessor : IActorObjectAccessor, ITagConsistentActorCommon
    {
        public string Head { get; set; } = "";
        public List<string?> Inputs { get; } = [];
        public int Cancelled, Confirmed, Notified;
        public bool NotifyThrows;
        public Task<ResultBox<T>> GetActorAsync<T>(string id) where T : class => Task.FromResult(ResultBox.FromValue((T)(object)this));
        public Task<bool> ActorExistsAsync(string id) => Task.FromResult(true);
        public Task<string> GetTagActorIdAsync() => Task.FromResult("Fence:test");
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => Task.FromResult(ResultBox.FromValue(Head));
        public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? position)
        {
            Inputs.Add(position);
            return Task.FromResult(ResultBox.FromValue(new TagWriteReservation(Guid.NewGuid().ToString(), DateTime.UtcNow.AddMinutes(1).ToString("O"), "Fence:test")));
        }
        public Task<bool> CancelReservationAsync(TagWriteReservation reservation) { Cancelled++; return Task.FromResult(true); }
        public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation) { Confirmed++; return Task.FromResult(true); }
        public Task NotifyEventWrittenAsync() { Notified++; if (NotifyThrows) throw new Exception("notify failed"); return Task.CompletedTask; }
    }

    private sealed class RecordingStore : IEventStore, IExpectedTagPositionEventStore, IWriteConditionCapabilityProvider, IConditionalEventStore
    {
        private readonly InMemoryConditionalEventStore _inner = new(Domain.EventTypes);
        public bool Enabled = true, Supported = true;
        public bool Throws, ReturnFalse;
        public Exception? Failure;
        public int LegacyWrites, WrittenCount, EpochChecks, ConditionalWrites;
        public ExpectedTagPositionSpecification? Specification;
        public WriteConditionCapabilityDescriptor DescribeWriteConditions() => Supported
            ? WriteConditionCapabilityDescriptor.Supporting("Recording", WriteConditionKind.ExpectedTagPosition, WriteConditionKind.SingleEventUniqueKey)
            : WriteConditionCapabilityDescriptor.None("Recording");
        public Task<ResultBox<bool>> EnsureExpectedTagPositionEnforcementEnabledAsync(CancellationToken ct = default)
        {
            EpochChecks++;
            return Task.FromResult(Enabled || ReturnFalse ? ResultBox.FromValue(Enabled) : ResultBox.Error<bool>(new TagHeadEnforcementNotEnabledException(Service)));
        }
        public async Task<ResultBox<ExpectedTagPositionWriteResult>> WriteSerializableEventsWithExpectedTagPositionsAsync(
            IReadOnlyList<SerializableEvent> events, ExpectedTagPositionSpecification specification, CancellationToken ct = default)
        {
            Specification = specification;
            WrittenCount = events.Count;
            if (Failure is not null)
            {
                if (Throws) throw Failure;
                return ResultBox.Error<ExpectedTagPositionWriteResult>(Failure);
            }
            var result = (await _inner.WriteSerializableEventsAsync(events)).GetValue();
            return ResultBox.FromValue(new ExpectedTagPositionWriteResult(result.Events, result.TagWrites));
        }
        public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(ConditionalAppendRequest request, CancellationToken ct = default)
        { ConditionalWrites++; return _inner.AppendIfUniqueAsync(request, ct); }
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => _inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? group = null) => _inner.GetAllTagsAsync(group);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) => _inner.ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? count) => _inner.ReadAllSerializableEventsAsync(since, count);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid id) => _inner.ReadSerializableEventAsync(id);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) => _inner.ReadSerializableEventsByTagAsync(tag, since);
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _inner.GetLatestSortableUniqueIdAsync();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        { LegacyWrites++; return _inner.WriteSerializableEventsAsync(events); }
    }

    private sealed class UnusedColdStorage : IColdObjectStorage
    {
        public Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResultBox<bool>> PutAsync(string path, Stream data, string? etag, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? etag, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct) => throw new NotSupportedException();
        public Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct) => throw new NotSupportedException();
    }
}
