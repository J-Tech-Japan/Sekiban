using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using System.Text;
using Xunit;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G15 conditional-append contract: the optional interface, the outcome machine, the fingerprint/receipt, the
///     runtime-resolved write-condition capability (fail-closed), and the InMemory reference. All additive — the existing
///     unconditional path is exercised alongside and is unchanged.
/// </summary>
public class ConditionalAppendContractTests
{
    private static DcbDomainTypes BuildDomainTypes()
    {
        var domainTypes = DomainType.GetDomainTypes();
        ((SimpleEventTypes)domainTypes.EventTypes).RegisterEventType<UniqueMarkerEvent>();
        ((SimpleEventTypes)domainTypes.EventTypes).RegisterEventType<GoldenEvent>();
        ((SimpleTagTypes)domainTypes.TagTypes).RegisterTagGroupType<MarkerTag>();
        return domainTypes;
    }

    private static (GeneralSekibanExecutor Executor, InMemoryConditionalEventStore Store, DcbDomainTypes Domain) NewConditional()
    {
        var domain = BuildDomainTypes();
        var store = new InMemoryConditionalEventStore(domain.EventTypes);
        var accessor = new InMemoryObjectAccessor(store, domain);
        return (new GeneralSekibanExecutor(store, accessor, domain), store, domain);
    }

    private static Func<MarkerCommand, ICommandContext, Task<ResultBox<EventOrNone>>> AppendMarker(string value) =>
        (_, ctx) => ctx.AppendEvent(new UniqueMarkerEvent(value), new MarkerTag("m"));

    // ---------------- Operation fingerprint ----------------

    [Fact]
    public void Fingerprint_IsStable_ForTheSameInputs_AndTagOrderIndependent()
    {
        var a = OperationFingerprint.ComputeFromCanonical("svc", "key-1", "Evt", "payload"u8, new[] { "A:1", "B:2" });
        var b = OperationFingerprint.ComputeFromCanonical("svc", "key-1", "Evt", "payload"u8, new[] { "B:2", "A:1" });
        Assert.Equal(a, b); // tag ordering must not change the fingerprint
    }

    [Theory]
    [InlineData("svc2", "key-1", "Evt", "payload", "A:1")] // different serviceId
    [InlineData("svc", "key-2", "Evt", "payload", "A:1")] // different key
    [InlineData("svc", "key-1", "Evt2", "payload", "A:1")] // different event type
    [InlineData("svc", "key-1", "Evt", "payload2", "A:1")] // different payload
    [InlineData("svc", "key-1", "Evt", "payload", "A:2")] // different tag
    public void Fingerprint_ChangesWhenAnyComponentChanges(string svc, string key, string type, string payload, string tag)
    {
        var baseline = OperationFingerprint.ComputeFromCanonical("svc", "key-1", "Evt", "payload"u8, new[] { "A:1" });
        var changed = OperationFingerprint.ComputeFromCanonical(svc, key, type, Encoding.UTF8.GetBytes(payload), new[] { tag });
        Assert.NotEqual(baseline, changed);
    }

    [Fact]
    public void NormalizeKey_TrimsAndAppliesNfc_AndRejectsBlankOrOversize()
    {
        Assert.Equal("key", OperationFingerprint.NormalizeKey("  key  "));
        Assert.Throws<ArgumentException>(() => OperationFingerprint.NormalizeKey("   "));
        Assert.Throws<ArgumentException>(() =>
            OperationFingerprint.NormalizeKey(new string('x', OperationFingerprint.MaxIdempotencyKeyUtf8Bytes + 1)));
    }

    // ---------------- Canonical fingerprint golden vectors ----------------

    [Fact]
    public void CanonicalizeJson_IsPropertyOrderAndWhitespaceIndependent()
    {
        var a = OperationFingerprint.CanonicalizeJson("{\"a\":1,\"b\":{\"y\":2,\"x\":3}}");
        var b = OperationFingerprint.CanonicalizeJson("{ \"b\" : { \"x\":3, \"y\":2 }, \"a\": 1 }");
        Assert.Equal(Encoding.UTF8.GetString(a), Encoding.UTF8.GetString(b));
    }

    [Fact]
    public void ComputeCanonical_SemanticallyEqualPayloads_DifferentFormatting_SameFingerprint()
    {
        var domain = BuildDomainTypes();
        // Two byte encodings of the SAME event: canonical domain form, and a reordered+indented reformat of it.
        var canonicalJson = domain.EventTypes.SerializeEventPayload(new GoldenEvent("x", 1));
        var reformatted = ReformatJsonReorderedIndented(canonicalJson);
        Assert.NotEqual(canonicalJson, reformatted); // genuinely different bytes going in

        var f1 = OperationFingerprint.ComputeCanonical("svc", "k", domain.EventTypes, nameof(GoldenEvent),
            Encoding.UTF8.GetBytes(canonicalJson), new[] { "Marker:m" }).GetValue();
        var f2 = OperationFingerprint.ComputeCanonical("svc", "k", domain.EventTypes, nameof(GoldenEvent),
            Encoding.UTF8.GetBytes(reformatted), new[] { "Marker:m" }).GetValue();
        Assert.Equal(f1, f2); // ...but the same fingerprint out
    }

    [Fact]
    public void ComputeCanonical_DifferentPayloadValue_DifferentFingerprint()
    {
        var domain = BuildDomainTypes();
        var f1 = OperationFingerprint.ComputeCanonical("svc", "k", domain.EventTypes, nameof(GoldenEvent),
            Encoding.UTF8.GetBytes(domain.EventTypes.SerializeEventPayload(new GoldenEvent("x", 1))), new[] { "Marker:m" }).GetValue();
        var f2 = OperationFingerprint.ComputeCanonical("svc", "k", domain.EventTypes, nameof(GoldenEvent),
            Encoding.UTF8.GetBytes(domain.EventTypes.SerializeEventPayload(new GoldenEvent("x", 2))), new[] { "Marker:m" }).GetValue();
        Assert.NotEqual(f1, f2);
    }

    [Fact]
    public void ComputeCanonical_UnregisteredType_FailsClosed()
    {
        var domain = BuildDomainTypes();
        var result = OperationFingerprint.ComputeCanonical("svc", "k", domain.EventTypes, "NotRegisteredEvent",
            Encoding.UTF8.GetBytes("{}"), Array.Empty<string>());
        Assert.False(result.IsSuccess);
        Assert.IsType<OperationCanonicalizationException>(result.GetException());
    }

    // ---------------- InMemory reference outcome machine ----------------

    [Fact]
    public async Task InMemory_FirstAppend_Wins_WithReceipt()
    {
        var (executor, store, _) = NewConditional();
        var result = await executor.ExecuteAsync(
            new MarkerCommand(),
            AppendMarker("v1"),
            new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-1") });

        Assert.True(result.IsSuccess);
        var exec = result.GetValue();
        Assert.Equal("Appended", exec.Metadata!["ConditionalAppendStatus"]);
        Assert.NotEqual(Guid.Empty, exec.EventId);
        var stored = (await store.ReadAllSerializableEventsAsync()).GetValue().ToList();
        Assert.Single(stored);
    }

    [Fact]
    public async Task InMemory_SameOperationRetry_ReturnsAlreadyCommitted_SameReceipt_NoSecondWrite()
    {
        var (executor, store, _) = NewConditional();
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-1") };

        var first = (await executor.ExecuteAsync(new MarkerCommand(), AppendMarker("same"), options)).GetValue();
        var second = (await executor.ExecuteAsync(new MarkerCommand(), AppendMarker("same"), options)).GetValue();

        Assert.Equal("Appended", first.Metadata!["ConditionalAppendStatus"]);
        Assert.Equal("AlreadyCommittedSameOperation", second.Metadata!["ConditionalAppendStatus"]);
        Assert.Equal(first.EventId, second.EventId); // the ORIGINAL winner
        Assert.Equal(first.SortableUniqueId, second.SortableUniqueId);
        Assert.Equal(first.Metadata!["OperationFingerprint"], second.Metadata!["OperationFingerprint"]);
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue()); // still only one durable event
    }

    [Fact]
    public async Task InMemory_SameKeyDifferentOperation_IsKeyReuseConflict_NoInnerException_NoSecondWrite()
    {
        var (executor, store, _) = NewConditional();
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-1") };

        Assert.True((await executor.ExecuteAsync(new MarkerCommand(), AppendMarker("first"), options)).IsSuccess);
        var conflict = await executor.ExecuteAsync(new MarkerCommand(), AppendMarker("DIFFERENT"), options);

        Assert.False(conflict.IsSuccess);
        var ex = Assert.IsType<KeyReuseConflictException>(conflict.GetException());
        Assert.Equal(ConditionalAppendStatus.KeyReuseConflict, ex.Status);
        Assert.Null(ex.InnerException); // conflict discovered by read — no provider exception fabricated
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task InMemory_DirectStore_ReceiptOutcomes()
    {
        var domain = BuildDomainTypes();
        var store = new InMemoryConditionalEventStore(domain.EventTypes);
        var evt = new Event(new UniqueMarkerEvent("x"), SortableUniqueId.GenerateNew(), nameof(UniqueMarkerEvent),
            Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), new List<string> { "Marker:m" });
        var serializable = evt.ToSerializableEvent(domain.EventTypes);

        var appended = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("k", serializable))).GetValue();
        Assert.Equal(ConditionalAppendStatus.Appended, appended.Status);
        Assert.Equal(evt.Id, appended.WinnerEventId);

        // Same key, same content but freshly-allocated ids => still recognised as the same operation.
        var retryEvt = serializable with { Id = Guid.CreateVersion7(), SortableUniqueIdValue = SortableUniqueId.GenerateNew() };
        var retry = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("k", retryEvt))).GetValue();
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, retry.Status);
        Assert.Equal(appended.WinnerEventId, retry.WinnerEventId); // original winner, not the retry's id
    }

    // ---------------- WriteCalls semantics: only successful durable writes are counted ----------------

    [Fact]
    public async Task WriteCalls_CountsOnlySuccessfulDurableWrites_AndRetryDoesNotRecount()
    {
        var domain = BuildDomainTypes();
        var store = new InMemoryConditionalEventStore(domain.EventTypes);
        var serializable = SampleSerializable(domain);

        var appended = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("k", serializable))).GetValue();
        Assert.Equal(ConditionalAppendStatus.Appended, appended.Status);
        Assert.Equal(1, store.WriteCalls);   // one successful durable write
        Assert.Equal(1, store.AppendAttempts);

        var retry = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("k", serializable))).GetValue();
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, retry.Status);
        Assert.Equal(1, store.WriteCalls);   // retry writes nothing durable
    }

    [Fact]
    public async Task FailFirstDurableWrite_LeavesNoClaim_ThenRetryWinsExactlyOnce_AndSameOpRetryDoesNotRecount()
    {
        var domain = BuildDomainTypes();
        var store = new InMemoryConditionalEventStore(domain.EventTypes);
        var serializable = SampleSerializable(domain);

        // Injected base-write failure: nothing durable happened.
        store.FailNextDurableWrite = true;
        var failed = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("k", serializable));
        Assert.False(failed.IsSuccess);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue()); // no event
        Assert.Equal(1, store.AppendAttempts);                                  // reached the write step
        Assert.Equal(0, store.WriteCalls);                                      // but not a durable success — no claim/receipt

        // Retry wins with a real durable write, exactly once.
        var retry = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("k", serializable))).GetValue();
        Assert.Equal(ConditionalAppendStatus.Appended, retry.Status);
        Assert.Equal(2, store.AppendAttempts);
        Assert.Equal(1, store.WriteCalls);
        var stored = (await store.ReadAllSerializableEventsAsync()).GetValue().ToList();
        Assert.Single(stored);
        Assert.Equal(serializable.Id, retry.WinnerEventId);                     // receipt names the stored winner
        Assert.Equal(stored[0].Id, retry.WinnerEventId);

        // A further same-operation retry returns the SAME receipt and increments neither counter.
        var sameOp = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("k", serializable))).GetValue();
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, sameOp.Status);
        Assert.Equal(retry.WinnerEventId, sameOp.WinnerEventId);
        Assert.Equal(retry.OperationFingerprint, sameOp.OperationFingerprint);
        Assert.Equal(2, store.AppendAttempts); // unchanged
        Assert.Equal(1, store.WriteCalls);     // unchanged
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    private static SerializableEvent SampleSerializable(DcbDomainTypes domain)
    {
        var evt = new Event(new UniqueMarkerEvent("x"), SortableUniqueId.GenerateNew(), nameof(UniqueMarkerEvent),
            Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), new List<string> { "Marker:m" });
        return evt.ToSerializableEvent(domain.EventTypes);
    }

    // ---------------- Capability descriptor (runtime-resolved, fail-closed) ----------------

    [Fact]
    public void Capability_InMemoryReference_Supports_SingleEventUniqueKey()
    {
        var domain = BuildDomainTypes();
        var store = new InMemoryConditionalEventStore(domain.EventTypes);
        var descriptor = SekibanDcbCapabilityResolver.DescribeWriteConditions(store, "event store");
        Assert.True(descriptor.Supports(WriteConditionKind.SingleEventUniqueKey));
    }

    [Fact]
    public void Capability_PlainStore_SupportsNothing_FailClosed()
    {
        var plain = new CoreInMemoryEventStore();
        var descriptor = SekibanDcbCapabilityResolver.DescribeWriteConditions(plain, "event store");
        Assert.False(descriptor.Supports(WriteConditionKind.SingleEventUniqueKey));
        Assert.False(descriptor.Supports(WriteConditionKind.Unknown)); // Unknown is never "supported"
    }

    [Fact]
    public void Capability_NullStore_SupportsNothing()
    {
        var descriptor = SekibanDcbCapabilityResolver.DescribeWriteConditions(null, "event store");
        Assert.Empty(descriptor.SupportedKinds);
    }

    [Fact]
    public void ExpectedTagPositionCapability_IsStructurallyPostgresOnly_ForEveryOtherProductionProvider()
    {
        // These are the actual provider store types, not a name-based convention. The executor's shared capability gate
        // therefore rejects before any write method for every listed provider; a future accidental implementation makes
        // this inventory fail and requires an explicit protocol decision.
        var providers = new[]
        {
            typeof(CoreInMemoryEventStore),
            typeof(SqliteEventStore),
            typeof(CosmosDbEventStore),
            typeof(DynamoDbEventStore)
        };
        Assert.All(providers, provider =>
            Assert.False(typeof(IExpectedTagPositionEventStore).IsAssignableFrom(provider),
                $"{provider.FullName} must fail closed for PostgreSQL expected-tag-position enforcement."));
    }

    [Fact]
    public void Capability_Composite_IsIntersectionOfUnderlying()
    {
        var supports = WriteConditionCapabilityDescriptor.Supporting("A", WriteConditionKind.SingleEventUniqueKey);
        var none = WriteConditionCapabilityDescriptor.None("B");
        var intersect = WriteConditionCapabilityDescriptor.Intersect("composite", new[] { supports, none });
        Assert.False(intersect.Supports(WriteConditionKind.SingleEventUniqueKey)); // all-underlying-support-only
    }

    /// <summary>
    ///     No default pass-through: any type that implements <see cref="IConditionalEventStore" /> MUST also declare the
    ///     capability via <see cref="IWriteConditionCapabilityProvider" />, so the runtime probe and the cast can never
    ///     disagree. Enforced by reflection over the production Core assembly and the testing reference assembly.
    /// </summary>
    [Fact]
    public void Architecture_EveryConditionalStore_AlsoDeclaresTheCapability()
    {
        var assemblies = new[] { typeof(HybridEventStore).Assembly, typeof(InMemoryConditionalEventStore).Assembly };
        foreach (var assembly in assemblies)
        {
            var offenders = assembly.GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false })
                .Where(t => typeof(IConditionalEventStore).IsAssignableFrom(t))
                .Where(t => !typeof(IWriteConditionCapabilityProvider).IsAssignableFrom(t))
                .Select(t => t.FullName)
                .ToList();
            Assert.True(offenders.Count == 0, $"IConditionalEventStore without IWriteConditionCapabilityProvider: {string.Join(", ", offenders)}");
        }
    }

    // ---------------- Executor fail-closed ordering ----------------

    [Fact]
    public async Task Executor_ConditionalOnUnsupportedStore_FailsClosed_BeforeHandlerOrWrite()
    {
        var domain = BuildDomainTypes();
        var plain = new CoreInMemoryEventStore(domain.EventTypes); // NOT conditional
        var accessor = new InMemoryObjectAccessor(plain, domain);
        var executor = new GeneralSekibanExecutor(plain, accessor, domain);

        var handlerInvoked = false;
        var result = await executor.ExecuteAsync(
            new MarkerCommand(),
            (MarkerCommand _, ICommandContext ctx) =>
            {
                handlerInvoked = true;
                return ctx.AppendEvent(new UniqueMarkerEvent("v"), new MarkerTag("m"));
            },
            new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-1") });

        Assert.False(result.IsSuccess);
        Assert.IsType<ConditionNotSupportedException>(result.GetException());
        Assert.False(handlerInvoked); // fail-closed BEFORE the handler runs
        Assert.Empty((await plain.ReadAllSerializableEventsAsync()).GetValue()); // nothing allocated or written
    }

    [Fact]
    public async Task Executor_ExpectedTagPositionOnUnsupportedStore_FailsClosed_BeforeHandlerOrWrite()
    {
        var domain = BuildDomainTypes();
        var plain = new CoreInMemoryEventStore(domain.EventTypes); // deliberately not PostgreSQL expected-position capable
        var accessor = new InMemoryObjectAccessor(plain, domain);
        var executor = new GeneralSekibanExecutor(plain, accessor, domain);
        var handlerInvoked = false;
        var options = new CommandExecutionOptions
        {
            ExpectedTagPositions = new ExpectedTagPositionSpecification(
                [new TagHeadExpectationEntry("default", "Marker:m", TagHeadExpectation.NoEnforcement())])
        };

        var result = await executor.ExecuteAsync(
            new MarkerCommand(),
            (MarkerCommand _, ICommandContext ctx) =>
            {
                handlerInvoked = true;
                return ctx.AppendEvent(new UniqueMarkerEvent("v"), new MarkerTag("m"));
            },
            options);

        Assert.False(result.IsSuccess);
        Assert.IsType<ConditionNotSupportedException>(result.GetException());
        Assert.False(handlerInvoked);
        Assert.Empty((await plain.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task Executor_ExpectedTagPositionOnActualSqliteStore_FailsClosed_BeforeHandlerOrWrite()
    {
        var domain = BuildDomainTypes();
        var sqlite = new SqliteEventStore(
            ":memory:",
            domain.EventTypes,
            new SqliteEventStoreOptions { AutoCreateDatabase = false });
        var executor = new GeneralSekibanExecutor(sqlite, new InMemoryObjectAccessor(sqlite, domain), domain);
        var handlerInvoked = false;

        var result = await executor.ExecuteAsync(
            new MarkerCommand(),
            (MarkerCommand _, ICommandContext ctx) =>
            {
                handlerInvoked = true;
                return ctx.AppendEvent(new UniqueMarkerEvent("v"), new MarkerTag("m"));
            },
            new CommandExecutionOptions
            {
                ExpectedTagPositions = new ExpectedTagPositionSpecification(
                    [new TagHeadExpectationEntry("default", "Marker:m", TagHeadExpectation.NoEnforcement())])
            });

        Assert.False(result.IsSuccess);
        Assert.IsType<ConditionNotSupportedException>(result.GetException());
        Assert.False(handlerInvoked);
    }

    [Fact]
    public async Task SerializedV2_UnsupportedInMemoryProvider_FailsClosedBeforeAnyProviderWrite()
    {
        var domain = BuildDomainTypes();
        var plain = new CoreInMemoryEventStore(domain.EventTypes);
        var executor = new GeneralSekibanExecutor(plain, new InMemoryObjectAccessor(plain, domain), domain);
        var acceptor = new SerializedCommitAcceptor(executor);
        var json = Encoding.UTF8.GetBytes(
            """{"version":2,"eventCandidates":[{"payload":"AQID","eventPayloadName":"UniqueMarkerEvent","tags":["Marker:m"]}],"consistencyTags":[{"tag":"Marker:m","lastSortableUniqueId":""}],"expectedTagPositions":[{"serviceId":"default","tag":"Marker:m","expectation":{"kind":2,"position":null}}]}""");

        var result = await acceptor.AcceptAsync(json);

        Assert.False(result.IsSuccess);
        Assert.IsType<ConditionNotSupportedException>(result.GetException());
        Assert.Empty((await plain.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task Executor_ConditionalAndExpectedPositionCombination_IsRejectedBeforeHandler()
    {
        var (executor, store, _) = NewConditional();
        var handlerInvoked = false;
        var result = await executor.ExecuteAsync(
            new MarkerCommand(),
            (MarkerCommand _, ICommandContext ctx) =>
            {
                handlerInvoked = true;
                return ctx.AppendEvent(new UniqueMarkerEvent("v"), new MarkerTag("m"));
            },
            new CommandExecutionOptions
            {
                ConditionalAppend = new ConditionalAppendSpecification("op"),
                ExpectedTagPositions = new ExpectedTagPositionSpecification(
                    [new TagHeadExpectationEntry("default", "Marker:m", TagHeadExpectation.NoEnforcement())])
            });

        Assert.False(result.IsSuccess);
        Assert.IsType<TagHeadExpectationValidationException>(result.GetException());
        Assert.False(handlerInvoked);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    // ---------------- Single-event contract (zero and multi both fail closed, before any store call) ----------------

    [Fact]
    public async Task Conditional_ZeroEvents_FailsClosed_NoStoreCall_NoReceipt()
    {
        var (executor, store, _) = NewConditional();
        var result = await executor.ExecuteAsync(
            new MarkerCommand(),
            (MarkerCommand _, ICommandContext _) => Task.FromResult(EventOrNone.None),
            new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-0") });

        // Must NOT fall through to the legacy empty-success result.
        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<SingleEventConditionalAppendException>(result.GetException());
        Assert.Equal(0, ex.AppendedEventCount);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue()); // no store call / no receipt
    }

    [Fact]
    public async Task Conditional_MultipleEvents_FailsClosed_NoStoreCall()
    {
        var (executor, store, _) = NewConditional();
        var result = await executor.ExecuteAsync(
            new MarkerCommand(),
            async (MarkerCommand _, ICommandContext ctx) =>
            {
                await ctx.AppendEvent(new UniqueMarkerEvent("a"), new MarkerTag("m"));
                await ctx.AppendEvent(new UniqueMarkerEvent("b"), new MarkerTag("m"));
                return EventOrNone.None;
            },
            new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-multi") });

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<SingleEventConditionalAppendException>(result.GetException());
        Assert.Equal(2, ex.AppendedEventCount);
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    // ---------------- Serialized (WASM) conditional path ----------------

    [Fact]
    public async Task Serialized_Conditional_UnsupportedVersion_FailsClosed_NoWrite()
    {
        var (executor, store, domain) = NewConditional();
        var payload = Encoding.UTF8.GetBytes(domain.EventTypes.SerializeEventPayload(new UniqueMarkerEvent("s")));
        var candidate = new SerializableEventCandidate(payload, nameof(UniqueMarkerEvent), new List<string> { "Marker:m" });
        var badVersion = new SerializedConditionalCommitRequest(SerializedConditionalCommitRequest.CurrentVersion + 1, candidate, "wasm-op");

        var result = await executor.CommitSerializableEventConditionallyAsync(badVersion);

        Assert.False(result.IsSuccess);
        Assert.IsType<UnsupportedSerializedCommitVersionException>(result.GetException());
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue()); // rejected before any write
    }

    [Fact]
    public async Task Serialized_Conditional_AppendedThenAlreadyCommitted()
    {
        var (executor, _, domain) = NewConditional();
        var payload = Encoding.UTF8.GetBytes(domain.EventTypes.SerializeEventPayload(new UniqueMarkerEvent("s")));
        var candidate = new SerializableEventCandidate(payload, nameof(UniqueMarkerEvent), new List<string> { "Marker:m" });
        var request = new SerializedConditionalCommitRequest(SerializedConditionalCommitRequest.CurrentVersion, candidate, "wasm-op");

        var first = (await executor.CommitSerializableEventConditionallyAsync(request)).GetValue();
        var second = (await executor.CommitSerializableEventConditionallyAsync(request)).GetValue();

        Assert.Equal(ConditionalAppendStatus.Appended, first.Status);
        Assert.Single(first.WrittenEvents);
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, second.Status);
        Assert.Empty(second.WrittenEvents); // nothing written on the retry
        Assert.Equal(first.WinnerEventId, second.WinnerEventId);
    }

    // ---------------- Compatibility: the unconditional path is unchanged and never capability-casts ----------------

    [Fact]
    public async Task Unconditional_Path_StillWorks_OnAPlainStore()
    {
        var domain = BuildDomainTypes();
        var plain = new CoreInMemoryEventStore(domain.EventTypes);
        var accessor = new InMemoryObjectAccessor(plain, domain);
        var executor = new GeneralSekibanExecutor(plain, accessor, domain);

        // No options overload => the legacy behaviour; a plain store that cannot condition writes is perfectly fine here.
        var result = await executor.ExecuteAsync(new MarkerCommand(), AppendMarker("legacy"));
        Assert.True(result.IsSuccess);
        Assert.Single((await plain.ReadAllSerializableEventsAsync()).GetValue());
    }

    // Re-emits JSON with object properties in REVERSE order and indented, so the bytes differ while the meaning does not.
    private static string ReformatJsonReorderedIndented(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var output = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(output, new System.Text.Json.JsonWriterOptions { Indented = true }))
        {
            WriteReversed(document.RootElement, writer);
        }

        return Encoding.UTF8.GetString(output.WrittenSpan);

        static void WriteReversed(System.Text.Json.JsonElement element, System.Text.Json.Utf8JsonWriter writer)
        {
            switch (element.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var prop in element.EnumerateObject().Reverse())
                    {
                        writer.WritePropertyName(prop.Name);
                        WriteReversed(prop.Value, writer);
                    }

                    writer.WriteEndObject();
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteReversed(item, writer);
                    }

                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }
    }

    private record UniqueMarkerEvent(string Value) : IEventPayload;

    private record GoldenEvent(string A, int B) : IEventPayload;

    private record MarkerCommand : ICommand;

    private record MarkerTag(string Id) : IStringTagGroup<MarkerTag>
    {
        public static string TagGroupName => "Marker";
        public static MarkerTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => false;
        public string GetId() => Id;
    }
}
