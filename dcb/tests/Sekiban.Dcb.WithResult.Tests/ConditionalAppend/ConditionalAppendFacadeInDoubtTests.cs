using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
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
///     state corruption) reaches the caller intact — right type, retryability, provider/service/derived-EventId, preserved
///     cause — with no generic wrapping and no leak of the raw idempotency key or payload.
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

    private static (GeneralSekibanExecutor Executor, DcbDomainTypes Domain) NewExecutor(
        Func<ConditionalAppendRequest, ResultBox<ConditionalAppendReceipt>> outcome)
    {
        var domain = BuildDomain();
        var inner = new InMemoryConditionalEventStore(domain.EventTypes);
        var store = new OutcomeForcingConditionalEventStore(inner, outcome);
        var accessor = new InMemoryObjectAccessor(store, domain);
        return (new GeneralSekibanExecutor(store, accessor, domain), domain);
    }

    private static ConditionalAppendInDoubtException InDoubt() =>
        ConditionalAppendInDoubtException.Create(
            "TestProvider", "svc-42", Guid.NewGuid(),
            ConditionalAppendInDoubtReason.WinnerUnreadableAfterConflict,
            new InvalidOperationException("provider conflict cause"));

    private static ConditionalAppendCommittedStateCorruptionException Corruption() =>
        new("TestProvider", "svc-42", Guid.NewGuid(), new InvalidOperationException("index corruption cause"));

    private static Func<MarkerCommand, ICommandContext, Task<ResultBox<EventOrNone>>> AppendMarker() =>
        (_, ctx) => ctx.AppendEvent(new UniqueMarkerEvent(SecretPayloadValue), new MarkerTag("m"));

    private static void AssertSecretSafe(Exception ex)
    {
        var text = ex.ToString();
        Assert.DoesNotContain(SecretKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretPayloadValue, text, StringComparison.Ordinal);
    }

    // ── WithResult facade ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithResult_InDoubt_IsErrorCarryingTheTypedException_NoGenericWrap()
    {
        var indoubt = InDoubt();
        var (executor, _) = NewExecutor(_ => ResultBox.Error<ConditionalAppendReceipt>(indoubt));
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification(SecretKey) };

        var result = await executor.ExecuteAsync(new MarkerCommand(), AppendMarker(), options);

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(result.GetException());
        Assert.True(ex.IsRetryable);
        Assert.Equal(ConditionalAppendInDoubtReason.WinnerUnreadableAfterConflict, ex.Reason);
        Assert.Equal("winner-unreadable-after-conflict", ex.ReasonCode);
        Assert.Equal("TestProvider", ex.ProviderName);
        Assert.Equal("svc-42", ex.ServiceId);
        Assert.NotEqual(Guid.Empty, ex.DerivedEventId);
        Assert.NotNull(ex.InnerException);
        AssertSecretSafe(ex);
    }

    [Fact]
    public async Task WithResult_CommittedStateCorruption_IsErrorCarryingTheTypedNonRetryableException()
    {
        var corruption = Corruption();
        var (executor, _) = NewExecutor(_ => ResultBox.Error<ConditionalAppendReceipt>(corruption));
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification(SecretKey) };

        var result = await executor.ExecuteAsync(new MarkerCommand(), AppendMarker(), options);

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendCommittedStateCorruptionException>(result.GetException());
        Assert.False(ex.IsRetryable);
        Assert.NotNull(ex.InnerException);
        AssertSecretSafe(ex);
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
    public async Task SerializedBoundary_InDoubt_IsErrorCarryingTheTypedException_SecretSafe()
    {
        var indoubt = InDoubt();
        var (executor, _) = NewExecutor(_ => ResultBox.Error<ConditionalAppendReceipt>(indoubt));

        var result = await ((ISerializedConditionalSekibanDcbExecutor)executor)
            .CommitSerializableEventConditionallyAsync(SerializedRequest());

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(result.GetException());
        Assert.True(ex.IsRetryable);
        Assert.Equal(ConditionalAppendInDoubtReason.WinnerUnreadableAfterConflict, ex.Reason);
        AssertSecretSafe(ex);
    }

    [Fact]
    public async Task SerializedBoundary_UnknownVersion_IsRejectedFailClosed()
    {
        var (executor, _) = NewExecutor(_ => throw new InvalidOperationException("should not be reached"));

        var result = await ((ISerializedConditionalSekibanDcbExecutor)executor)
            .CommitSerializableEventConditionallyAsync(SerializedRequest(version: 999));

        Assert.False(result.IsSuccess);
        Assert.IsType<UnsupportedSerializedCommitVersionException>(result.GetException());
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
