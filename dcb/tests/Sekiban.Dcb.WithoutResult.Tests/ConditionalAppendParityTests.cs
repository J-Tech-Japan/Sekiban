using Dcb.Domain.WithoutResult;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.TestSupport;
using Xunit;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
namespace Sekiban.Dcb.WithoutResult.Tests;

/// <summary>
///     Facade parity for SEK-G15 on the exception-throwing side: the same outcome machine, but the two failure states are
///     THROWN through the guarded boundary (preserving the original typed exception) rather than returned.
/// </summary>
public class ConditionalAppendParityTests
{
    private static DcbDomainTypes BuildDomain()
    {
        var domain = DomainType.GetDomainTypes();
        ((SimpleEventTypes)domain.EventTypes).RegisterEventType<UniqueMarkerEvent>();
        ((SimpleTagTypes)domain.TagTypes).RegisterTagGroupType<MarkerTag>();
        return domain;
    }

    private static Func<MarkerCommand, ICommandContext, Task<EventOrNone>> AppendMarker(string value) =>
        (_, ctx) => ctx.AppendEvent(new UniqueMarkerEvent(value), new MarkerTag("m"));

    [Fact]
    public async Task Conditional_AppendedThenAlreadyCommitted_DoNotThrow()
    {
        var domain = BuildDomain();
        var store = new InMemoryConditionalEventStore(domain.EventTypes);
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, domain), domain);
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-1") };

        var first = await executor.ExecuteAsync(new MarkerCommand(), AppendMarker("same"), options);
        var second = await executor.ExecuteAsync(new MarkerCommand(), AppendMarker("same"), options);

        Assert.Equal("Appended", first.Metadata!["ConditionalAppendStatus"]);
        Assert.Equal("AlreadyCommittedSameOperation", second.Metadata!["ConditionalAppendStatus"]);
        Assert.Equal(first.EventId, second.EventId);
    }

    [Fact]
    public async Task Conditional_OnUnsupportedStore_ThrowsConditionNotSupported()
    {
        var domain = BuildDomain();
        var plain = new CoreInMemoryEventStore(domain.EventTypes);
        var executor = new GeneralSekibanExecutor(plain, new InMemoryObjectAccessor(plain, domain), domain);
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-1") };

        await Assert.ThrowsAsync<ConditionNotSupportedException>(
            () => executor.ExecuteAsync(new MarkerCommand(), AppendMarker("v"), options));
    }

    [Fact]
    public async Task Conditional_KeyReuseConflict_Throws()
    {
        var domain = BuildDomain();
        var store = new InMemoryConditionalEventStore(domain.EventTypes);
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, domain), domain);
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-1") };

        await executor.ExecuteAsync(new MarkerCommand(), AppendMarker("first"), options);
        await Assert.ThrowsAsync<KeyReuseConflictException>(
            () => executor.ExecuteAsync(new MarkerCommand(), AppendMarker("DIFFERENT"), options));
    }

    [Fact]
    public async Task Conditional_InDoubt_ThrowsTypedRetryable_NotGenericallyWrapped()
    {
        var domain = BuildDomain();
        var indoubt = ConditionalAppendInDoubtException.Create(
            "TestProvider", "svc-42", Guid.NewGuid(),
            ConditionalAppendInDoubtReason.AmbiguousAfterWrite, new InvalidOperationException("cause"));
        var store = new OutcomeForcingConditionalEventStore(
            new InMemoryConditionalEventStore(domain.EventTypes),
            _ => ResultBoxes.ResultBox.Error<ConditionalAppendReceipt>(indoubt));
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, domain), domain);
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-1") };

        var ex = await Assert.ThrowsAsync<ConditionalAppendInDoubtException>(
            () => executor.ExecuteAsync(new MarkerCommand(), AppendMarker("v"), options));
        Assert.True(ex.IsRetryable);
        Assert.Equal(ConditionalAppendInDoubtReason.AmbiguousAfterWrite, ex.Reason);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public async Task Conditional_CommittedStateCorruption_ThrowsTypedNonRetryable()
    {
        var domain = BuildDomain();
        var corruption = new ConditionalAppendCommittedStateCorruptionException(
            "TestProvider", "svc-42", Guid.NewGuid(), new InvalidOperationException("corrupt"));
        var store = new OutcomeForcingConditionalEventStore(
            new InMemoryConditionalEventStore(domain.EventTypes),
            _ => ResultBoxes.ResultBox.Error<ConditionalAppendReceipt>(corruption));
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, domain), domain);
        var options = new CommandExecutionOptions { ConditionalAppend = new ConditionalAppendSpecification("op-1") };

        var ex = await Assert.ThrowsAsync<ConditionalAppendCommittedStateCorruptionException>(
            () => executor.ExecuteAsync(new MarkerCommand(), AppendMarker("v"), options));
        Assert.False(ex.IsRetryable);
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
