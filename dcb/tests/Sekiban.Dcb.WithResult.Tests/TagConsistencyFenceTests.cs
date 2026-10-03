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

    private static (RecordingStore Store, RecordingAccessor Accessor, GeneralSekibanExecutor Executor) Setup()
    {
        var store = new RecordingStore();
        var accessor = new RecordingAccessor();
        return (store, accessor, Executor(store, accessor));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnforcedTagLimit_RejectsBeforeReservation_AndExplicitBeforeHandler(bool explicitSpec)
    {
        var store = new RecordingStore { Limits = new(1, null) };
        var accessor = new RecordingAccessor();
        var tags = new[] { new Tag("first"), new Tag("second") };
        var specification = new ExpectedTagPositionSpecification(tags.Select(tag =>
            new TagHeadExpectationEntry(Service, ((ITag)tag).GetTag(), TagHeadExpectation.AssertEmpty())).ToArray());
        var handled = false;
        var result = await Executor(store, accessor).ExecuteAsync(new Command(), async (_, ctx) =>
        {
            handled = true;
            foreach (var tag in tags) await ctx.TagExistsAsync(tag);
            return EventOrNone.EventWithTags(Payload(), tags);
        }, new CommandExecutionOptions { ExpectedTagPositions = explicitSpec ? specification : null });

        AssertLimitRejection(result.GetException(), store, accessor,
            nameof(ExpectedTagPositionLimits.MaxEnforcedTagsPerWrite), 1, ["Fence:first", "Fence:second"]);
        Assert.Equal(!explicitSpec, handled);
        Assert.Equal(explicitSpec ? 0 : 1, store.EpochChecks);
    }

    [Fact]
    public async Task NoEnforcementEntries_DoNotConsumeEnforcedTagLimit()
    {
        var store = new RecordingStore { Limits = new(1, null) };
        var tags = Enumerable.Range(0, 4).Select(i => new Tag($"tag-{i}")).ToArray();
        var specification = new ExpectedTagPositionSpecification(tags.Select((tag, i) =>
            new TagHeadExpectationEntry(Service, ((ITag)tag).GetTag(),
                i == 0 ? TagHeadExpectation.AssertEmpty() : TagHeadExpectation.NoEnforcement())).ToArray());
        var result = await Executor(store, new()).ExecuteAsync(new Command(),
            (_, _) => Task.FromResult(EventOrNone.EventWithTags(Payload(), tags)),
            new CommandExecutionOptions { ExpectedTagPositions = specification });
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Same(specification, store.Specification);
        Assert.Equal(1, store.WrittenCount);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task TotalTagLimit_IncludesNonConsistencyTags_EvenWithoutDerivedSpecification(
        bool explicitSpec, bool consistencyTag)
    {
        var store = new RecordingStore { Limits = new(null, 1) };
        var accessor = new RecordingAccessor();
        var tag = new Tag("first", consistencyTag);
        var specification = new ExpectedTagPositionSpecification(
            [new(Service, ((ITag)tag).GetTag(), TagHeadExpectation.NoEnforcement())]);
        var handled = false;
        var result = await Executor(store, accessor).ExecuteAsync(new Command(), (_, _) =>
        {
            handled = true;
            return Task.FromResult(EventOrNone.Event(Payload(), tag, new Tag("index", false)));
        }, new CommandExecutionOptions { ExpectedTagPositions = explicitSpec ? specification : null });
        Assert.True(handled);
        AssertLimitRejection(result.GetException(), store, accessor,
            nameof(ExpectedTagPositionLimits.MaxTagsPerWrite), 1, ["Fence:first", "Fence:index"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TotalTagLimit_CountsDistinctTagStringsAcrossEvents(bool serialized)
    {
        var store = new RecordingStore { Limits = new(null, 1) };
        var executor = Executor(store, new());
        if (serialized)
        {
            var request = Request();
            var result = await executor.CommitSerializableEventsAsync(request with
            { EventCandidates = [request.EventCandidates[0], request.EventCandidates[0]] });
            Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        }
        else
        {
            var result = await executor.ExecuteCommandAsync(async ctx =>
            {
                await ctx.AppendEvent(Payload(), [new Tag("same")]);
                await ctx.AppendEvent(Payload(), [new Tag("same")]);
                return EventOrNone.None;
            });
            Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        }
        Assert.Equal(2, store.WrittenCount);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public async Task SerializedLimits_RejectBeforeReservation(bool v2, bool totalLimit, bool consistencyTags)
    {
        var store = new RecordingStore { Limits = totalLimit ? new(null, 1) : new(1, null) };
        var accessor = new RecordingAccessor();
        var request = Request();
        request = request with
        {
            EventCandidates = [request.EventCandidates[0] with { Tags = ["Fence:serialized", "Fence:second"] }],
            ConsistencyTags = !consistencyTags ? [] : totalLimit
                ? request.ConsistencyTags
                : [request.ConsistencyTags[0], new("Fence:second", "")]
        };
        var executor = Executor(store, accessor);
        var result = v2
            ? await executor.CommitSerializableEventsWithExpectedTagPositionsAsync(new(2,
                request.EventCandidates, request.ConsistencyTags, request.ConsistencyTags.Select(entry =>
                    new TagHeadExpectationEntry(Service, entry.Tag, TagHeadExpectation.AssertEmpty())).ToArray()))
            : await executor.CommitSerializableEventsAsync(request);
        AssertLimitRejection(result.GetException(), store, accessor,
            totalLimit ? nameof(ExpectedTagPositionLimits.MaxTagsPerWrite) : nameof(ExpectedTagPositionLimits.MaxEnforcedTagsPerWrite),
            1, ["Fence:second", "Fence:serialized"]);
        Assert.Equal(v2 && !totalLimit ? 0 : 1, store.EpochChecks);
    }

    [Fact]
    public async Task WithoutResult_LimitRejectionPreservesTypedException()
    {
        var store = new RecordingStore { Limits = new(0, null) };
        var accessor = new RecordingAccessor();
        var executor = new WithoutGeneral(store, accessor, Domain);
        var specification = new ExpectedTagPositionSpecification(
            [new(Service, "Fence:limited", TagHeadExpectation.AssertEmpty())]);
        var exception = await Assert.ThrowsAsync<TagHeadEnforcementLimitExceededException>(() =>
            executor.ExecuteAsync(new Command(), (_, _) => throw new Exception("handler must not run"),
                new CommandExecutionOptions { ExpectedTagPositions = specification }));
        AssertLimitRejection(exception, store, accessor,
            nameof(ExpectedTagPositionLimits.MaxEnforcedTagsPerWrite), 0, ["Fence:limited"]);
        Assert.Equal(0, store.EpochChecks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hybrid_ForwardsHotStoreLimitsOrUnlimited(bool supported)
    {
        var limits = new ExpectedTagPositionLimits(1, 10);
        IEventStore hot = supported ? new RecordingStore { Limits = limits }
            : new Sekiban.Dcb.Testing.InMemoryEventStore(Domain.EventTypes);
        var hybrid = new HybridEventStore(hot, new UnusedColdStorage(), new JsonlColdSegmentFormatHandler(),
            new DefaultServiceIdProvider(), Options.Create(new ColdEventStoreOptions()),
            NullLogger<HybridEventStore>.Instance);
        Assert.Same(supported ? limits : ExpectedTagPositionLimits.Unlimited, hybrid.ExpectedTagPositionLimits);
    }

    private static void AssertLimitRejection(Exception exception, RecordingStore store, RecordingAccessor accessor,
        string limitName, int limit, string[] tags)
    {
        var exceeded = Assert.IsType<TagHeadEnforcementLimitExceededException>(exception);
        Assert.Equal("Recording", exceeded.ProviderName);
        Assert.Equal(limitName, exceeded.LimitName);
        Assert.Equal(limit, exceeded.Limit);
        Assert.Equal(tags.Length, exceeded.Actual);
        Assert.Equal(tags, exceeded.Tags);
        Assert.Empty(accessor.Inputs);
        Assert.Null(store.Specification);
        Assert.Equal(0, store.WrittenCount);
        Assert.Equal(0, store.LegacyWrites);
        Assert.Equal(0, store.ConditionalWrites);
    }

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
        var (store, accessor, executor) = Setup();
        var inner = new Tag("ambiguous");
        var result = await executor.ExecuteCommandAsync(async ctx =>
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

    [Theory]
    [InlineData("with-result", false)]
    [InlineData("without-result", false)]
    [InlineData("core", false)]
    [InlineData("with-result", true)]
    [InlineData("without-result", true)]
    [InlineData("core", true)]
    public async Task PerCommandFence_UsesStoreServiceId_WithLegacyConstructionOrDi(string facade, bool di)
    {
        var store = new RecordingStore { ExpectedTagPositionServiceId = "tenant-x" };
        var accessor = new RecordingAccessor();
        var type = facade switch
        {
            "with-result" => typeof(GeneralSekibanExecutor),
            "without-result" => typeof(WithoutGeneral),
            _ => typeof(CoreGeneralSekibanExecutor)
        };
        // No fence options or service-id provider are registered: DI must use a legacy constructor.
        using var provider = DiProvider(type, store, false, false, false, false, false);
        var executor = di ? provider.GetRequiredService(type) : facade switch
        {
            "with-result" => (object)new GeneralSekibanExecutor(store, accessor, Domain),
            "without-result" => new WithoutGeneral(store, accessor, Domain),
            _ => new CoreGeneralSekibanExecutor(store, accessor, Domain)
        };
        var options = new CommandExecutionOptions { TagConsistencyFence = TagConsistencyFenceMode.DeriveFromReservations };
        var tag = new Tag("store-service");
        switch (executor)
        {
            case GeneralSekibanExecutor withResult:
                Assert.True((await withResult.ExecuteAsync(new Command(),
                    (_, _) => Task.FromResult(EventOrNone.Event(Payload(), tag)), options)).IsSuccess);
                break;
            case WithoutGeneral withoutResult:
                await withoutResult.ExecuteAsync(new Command(),
                    (_, _) => Task.FromResult(EventOrNone.Event(Payload(), tag).GetValue()), options);
                break;
            case CoreGeneralSekibanExecutor core:
                Assert.True((await core.ExecuteAsync(new Command(),
                    (_, _) => Task.FromResult(EventOrNone.Event(Payload(), tag)), options)).IsSuccess);
                break;
        }
        Assert.Equal("tenant-x", Assert.Single(store.Specification!.Entries).ServiceId);
        Assert.Equal(0, store.LegacyWrites);
    }

    [Theory]
    [InlineData("TENANT-X", "tenant-x")]
    [InlineData(null, Service)]
    public async Task SerializedFence_NormalizesStoreServiceId_OrFallsBackWhenNotExposed(string? exposed, string expected)
    {
        var store = new RecordingStore { ExpectedTagPositionServiceId = exposed };
        var result = await Executor(store, new()).CommitSerializableEventsAsync(Request());
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(expected, Assert.Single(store.Specification!.Entries).ServiceId);
    }

    [Fact]
    public async Task InvalidExposedStoreServiceId_DoesNotFallBackOrWrite()
    {
        var store = new RecordingStore { ExpectedTagPositionServiceId = "invalid/service" };
        var result = await Executor(store, new()).CommitSerializableEventsAsync(Request());
        Assert.IsType<ArgumentException>(result.GetException());
        Assert.Null(store.Specification);
        Assert.Equal(0, store.LegacyWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hybrid_ExposesHotStoreServiceIdOnly(bool supported)
    {
        IEventStore hot = supported
            ? new RecordingStore { ExpectedTagPositionServiceId = "tenant-x" }
            : new Sekiban.Dcb.Testing.InMemoryEventStore(Domain.EventTypes);
        var hybrid = new HybridEventStore(hot, new UnusedColdStorage(), new JsonlColdSegmentFormatHandler(),
            new FixedServiceIdProvider("hybrid-service"), Options.Create(new ColdEventStoreOptions()),
            NullLogger<HybridEventStore>.Instance);
        Assert.Equal(supported ? "tenant-x" : null, hybrid.ExpectedTagPositionServiceId);
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
        AssertConflictCleanup(accessor);
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
        AssertConflictCleanup(accessor);
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
        var (store, accessor, executor) = Setup();
        var result = await executor.CommitSerializableEventsAsync(Request(position));
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(position == "" ? TagHeadExpectation.AssertEmpty() : TagHeadExpectation.Exact(position),
            Assert.Single(store.Specification!.Entries).Expectation);
        Assert.Equal(position, Assert.Single(accessor.Inputs));
    }

    [Fact]
    public async Task SerializedV1_PreservesNullAndDuplicateRejection_AndNoOp()
    {
        var (store, accessor, executor) = Setup();
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
        var (store, accessor, executor) = Setup();
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
        foreach (var customService in new[] { false, true })
            yield return [type, gate, fence, allocator, optionalServices, customService];
    }

    [Theory]
    [MemberData(nameof(DiCases))]
    public async Task DiResolution_HonorsFenceGateAndServiceId(
        Type type, bool gate, bool fence, bool allocator, bool optionalServices, bool customService)
    {
        var store = new RecordingStore();
        using var provider = DiProvider(type, store, gate, fence, allocator, optionalServices, customService);
        var executor = provider.GetRequiredService(type);
        var result = executor is CoreGeneralSekibanExecutor core
            ? await core.CommitSerializableEventsAsync(Request(), default)
            : await ((ISerializedSekibanDcbExecutor)executor).CommitSerializableEventsAsync(Request());
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Equal(fence, store.Specification is not null);
        if (fence)
            Assert.Equal(customService ? "tenant-x" : Service, Assert.Single(store.Specification!.Entries).ServiceId);

        store.Supported = false;
        if (fence)
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService(type));
        else
            Assert.NotNull(provider.GetRequiredService(type));
    }

    private static ServiceProvider DiProvider(
        Type type, RecordingStore store, bool gate, bool fence, bool allocator, bool optionalServices, bool customService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Domain);
        services.AddSingleton<IEventStore>(store);
        services.AddSingleton<IActorObjectAccessor>(new RecordingAccessor());
        if (optionalServices)
        {
            services.AddSingleton<IEventPublisher, NoPublisher>();
            services.AddSingleton<IExecutedUserProvider, NoUser>();
        }
        if (allocator) services.AddSekibanDcbSortableUniqueIdGenerator();
        if (customService) services.AddSingleton<IServiceIdProvider>(new FixedServiceIdProvider("tenant-x"));
        if (gate) services.AddSekibanDcbExecutorSizeGate(_ => { });
        if (fence) services.AddSekibanDcbTagConsistencyFence(o => o.Mode = TagConsistencyFenceMode.DeriveFromReservations);
        services.AddTransient(type);
        return services.BuildServiceProvider();
    }

    private sealed class NoPublisher : IEventPublisher
    {
        public Task PublishAsync(IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events, CancellationToken ct = default) => Task.CompletedTask;
    }
    private sealed class NoUser : IExecutedUserProvider { public string GetExecutedUser() => "test"; }

    private static void AssertConflictCleanup(RecordingAccessor accessor)
    {
        Assert.Equal(1, accessor.Cancelled);
        Assert.Equal(0, accessor.Confirmed);
        Assert.Equal(1, accessor.Notified);
    }

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
        public string? ExpectedTagPositionServiceId { get; init; }
        public ExpectedTagPositionLimits Limits { get; init; } = ExpectedTagPositionLimits.Unlimited;
        public ExpectedTagPositionLimits ExpectedTagPositionLimits => Limits;
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
