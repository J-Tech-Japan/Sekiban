using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.TestSupport;
using System.Text;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 typed-failure propagation through the WithResult facade and the versioned serialized conditional boundary.
///     A store double forces a specific conditional outcome; the tests assert the TYPED exception (in-doubt or committed-
///     state corruption) reaches the caller as the EXACT SAME instance (and its cause), with the closed reason, provider/
///     service/derived-EventId, no generic wrapping, and — checked recursively over the whole exception graph — no leak of
///     the raw idempotency key or payload. The unknown serialized version is rejected first, with zero store/capability
///     side effects.
/// </summary>
public class ConditionalAppendFacadeInDoubtTests
{
    private const string SecretKey = "op-secret-key-9F3";
    private const string SecretPayloadValue = "TOP-SECRET-VALUE";

    private static DcbDomainTypes BuildDomain()
    {
        var domain = DomainType.GetDomainTypes();
        ((SimpleEventTypes)domain.EventTypes).RegisterEventType<UniqueMarkerEvent>();
        ((SimpleTagTypes)domain.TagTypes).RegisterTagGroupType<MarkerTag>();
        return domain;
    }

    private static (GeneralSekibanExecutor Executor, OutcomeForcingConditionalEventStore Store) NewExecutor(
        Func<ConditionalAppendRequest, ResultBox<ConditionalAppendReceipt>> outcome)
    {
        var domain = BuildDomain();
        var inner = new InMemoryConditionalEventStore(domain.EventTypes);
        var store = new OutcomeForcingConditionalEventStore(inner, outcome);
        var accessor = new InMemoryObjectAccessor(store, domain);
        return (new GeneralSekibanExecutor(store, accessor, domain), store);
    }

    private static Func<MarkerCommand, ICommandContext, Task<ResultBox<EventOrNone>>> AppendMarker() =>
        (_, ctx) => ctx.AppendEvent(new UniqueMarkerEvent(SecretPayloadValue), new MarkerTag("m"));

    // ── WithResult facade ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithResult_InDoubt_ReturnsExactSameTypedException_AndCause_SecretSafe_NoWrap()
    {
        var cause = new InvalidOperationException("provider conflict cause");
        var indoubt = ConditionalAppendInDoubtException.Create(
            "TestProvider", "svc-42", Guid.NewGuid(), ConditionalAppendInDoubtReason.WinnerUnreadableAfterConflict, cause);
        var (executor, _) = NewExecutor(_ => ResultBox.Error<ConditionalAppendReceipt>(indoubt));
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification(SecretKey) };

        var result = await executor.ExecuteAsync(new MarkerCommand(), AppendMarker(), options);

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(result.GetException());
        Assert.Same(indoubt, ex);                    // exact instance, no generic wrap
        Assert.Same(cause, ex.InnerException);        // original provider cause preserved by identity
        Assert.True(ex.IsRetryable);
        Assert.Equal(ConditionalAppendInDoubtReason.WinnerUnreadableAfterConflict, ex.Reason);
        Assert.Equal("winner-unreadable-after-conflict", ex.ReasonCode);
        Assert.Equal("TestProvider", ex.ProviderName);
        Assert.Equal("svc-42", ex.ServiceId);
        Assert.NotEqual(Guid.Empty, ex.DerivedEventId);
        ExceptionGraphSecretAssert.ContainsNoneOf(ex, SecretKey, SecretPayloadValue);
    }

    [Fact]
    public async Task WithResult_CommittedStateCorruption_ReturnsExactSameTypedNonRetryableException_SecretSafe()
    {
        var corruption = new ConditionalAppendCommittedStateCorruptionException(
            "TestProvider", "svc-42", Guid.NewGuid(), "derived-row-hash-abc");
        var (executor, _) = NewExecutor(_ => ResultBox.Error<ConditionalAppendReceipt>(corruption));
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification(SecretKey) };

        var result = await executor.ExecuteAsync(new MarkerCommand(), AppendMarker(), options);

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendCommittedStateCorruptionException>(result.GetException());
        Assert.Same(corruption, ex);
        Assert.False(ex.IsRetryable);
        Assert.Null(ex.InnerException);              // corruption never chains an unsafe provider exception
        Assert.Equal("derived-row-hash-abc", ex.CorruptRowId);
        ExceptionGraphSecretAssert.ContainsNoneOf(ex, SecretKey, SecretPayloadValue);
    }

    // ── Versioned serialized boundary ───────────────────────────────────────────────────────────────

    private static SerializedConditionalCommitRequest SerializedRequest(int version = SerializedConditionalCommitRequest.CurrentVersion) =>
        new(version,
            new SerializableEventCandidate(
                Encoding.UTF8.GetBytes($$"""{"Value":"{{SecretPayloadValue}}"}"""),
                nameof(UniqueMarkerEvent),
                new List<string> { "Marker:m" }),
            SecretKey);

    [Fact]
    public async Task SerializedBoundary_InDoubt_ReturnsExactSameTypedException_SecretSafe()
    {
        var cause = new InvalidOperationException("provider conflict cause");
        var indoubt = ConditionalAppendInDoubtException.Create(
            "TestProvider", "svc-42", Guid.NewGuid(), ConditionalAppendInDoubtReason.AmbiguousAfterWrite, cause);
        var (executor, _) = NewExecutor(_ => ResultBox.Error<ConditionalAppendReceipt>(indoubt));

        var result = await ((ISerializedConditionalSekibanDcbExecutor)executor)
            .CommitSerializableEventConditionallyAsync(SerializedRequest());

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(result.GetException());
        Assert.Same(indoubt, ex);
        Assert.Same(cause, ex.InnerException);
        Assert.True(ex.IsRetryable);
        ExceptionGraphSecretAssert.ContainsNoneOf(ex, SecretKey, SecretPayloadValue);
    }

    [Fact]
    public async Task SerializedBoundary_UnknownVersion_IsRejectedFirst_WithZeroSideEffectsAtEveryPreflightStage()
    {
        var (executor, store) = NewExecutor(_ => throw new InvalidOperationException("store must not be reached"));

        // DIRECT probes for the allocation stage: install counting EventId/SortableUniqueId factories on the underlying
        // core executor (reached by reflection, so no cross-assembly type/IVT coupling). If validation moved after
        // allocation, these would be > 0.
        const System.Reflection.BindingFlags nonPublicInstance =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var core = typeof(GeneralSekibanExecutor).GetField("_core", nonPublicInstance)!.GetValue(executor)!;
        var eventIdAllocations = 0;
        var sortableAllocations = 0;
        core.GetType().GetProperty("ConditionalEventIdFactory", nonPublicInstance)!
            .SetValue(core, (Func<Guid>)(() => { eventIdAllocations++; return Guid.CreateVersion7(); }));
        core.GetType().GetProperty("ConditionalSortableIdFactory", nonPublicInstance)!
            .SetValue(core, (Func<string>)(() => { sortableAllocations++; return SortableUniqueId.GenerateNew(); }));

        var result = await ((ISerializedConditionalSekibanDcbExecutor)executor)
            .CommitSerializableEventConditionallyAsync(SerializedRequest(version: 999));

        Assert.False(result.IsSuccess);
        Assert.IsType<UnsupportedSerializedCommitVersionException>(result.GetException());
        // Every preflight stage the packet names, proven zero directly and non-vacuously (moving the version check after
        // any of these would trip the corresponding counter):
        Assert.Equal(0, store.DescribeCalls);   // capability resolution
        Assert.Equal(0, eventIdAllocations);     // EventId allocation
        Assert.Equal(0, sortableAllocations);    // SortableUniqueId allocation
        Assert.Equal(0, store.ReadCalls);        // provider reads (serialization/canonicalization happen inside the append)
        Assert.Equal(0, store.WriteCalls);       // unconditional writes
        Assert.Equal(0, store.AppendAttempts);   // conditional append (payload canonicalization happens inside it)
    }

    private record UniqueMarkerEvent(string Value) : IEventPayload;

    private record MarkerCommand : ICommand;

    private record MarkerTag(string Id) : IStringTagGroup<MarkerTag>
    {
        public static string TagGroupName => "Marker";
        public static MarkerTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => false;
        public string GetId() => Id;
    }
}
