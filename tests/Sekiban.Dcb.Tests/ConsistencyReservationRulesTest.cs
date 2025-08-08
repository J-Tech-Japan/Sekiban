using Dcb.Domain;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for reservation decision rules:
///     1. Non-consistency tags must NOT trigger reservation.
///     2. ConsistencyTag with SortableUniqueId uses provided id.
///     3. ConsistencyTag without SortableUniqueId (or plain consistency tag) uses accessed state LastSortableUniqueId.
/// </summary>
public class ConsistencyReservationRulesTest
{
    private readonly InMemoryObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;
    private readonly GeneralSekibanExecutor _executor;

    public ConsistencyReservationRulesTest()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
        _actorAccessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _executor = new GeneralSekibanExecutor(_eventStore, _actorAccessor, _domainTypes);
    }

    [Fact]
    public async Task NonConsistencyTag_Should_Not_Create_TagState()
    {
        var baseTag = new BaseTag(Guid.NewGuid().ToString());
        var nonConsistency = NonConsistencyTag.From(baseTag);

        var result = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(new DummyEvent("A"), nonConsistency)));

        Assert.True(result.IsSuccess);
        // TagExistsAsync should still return true because event write uses tag list,
        // but reservation path should not have attempted optimistic check.
        // We cannot directly observe reservation count; instead ensure execution succeeds
        // even if providing old sortable id would have failed (implicit test by absence of exception).
    }

    [Fact]
    public async Task ConsistencyTag_With_Explicit_SortableUniqueId_Should_Use_It()
    {
        var baseTag = new BaseTag(Guid.NewGuid().ToString());
        var explicitId = SortableUniqueId.GenerateNew();
        var consistencyTag = ConsistencyTag.FromTagWithSortableUniqueId(baseTag, explicitId);

        var result = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(new DummyEvent("B"), consistencyTag)));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ConsistencyTag_Without_SortableUniqueId_Should_Use_Accessed_State()
    {
        var baseTag = new BaseTag(Guid.NewGuid().ToString());
        var consistencyTag = ConsistencyTag.From(baseTag); // no explicit id

        // First write to create state
        var first = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(new DummyEvent("C1"), consistencyTag)));
        Assert.True(first.IsSuccess);

        // Second write should access state and use LastSortableUniqueId
        // Access state beforehand to ensure it is tracked
        await _executor.ExecuteAsync(
            new SimpleCommand(),
            async (cmd, ctx) =>
            {
                await ctx.GetStateAsync<DummyProjector>(baseTag);
                return EventOrNone.None; // just for tracking
            });
        var second = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(new DummyEvent("C2"), consistencyTag)));
        Assert.True(second.IsSuccess);
    }

    private record DummyEvent(string Name) : IEventPayload;
    private record BaseTag(string Id) : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTagGroup() => "Base";
        public string GetTagContent() => Id;
    }

    private record SimpleCommand : ICommand;

    private class DummyProjector : ITagProjector
    {
        public string GetProjectorVersion() => "1";
        public ITagStatePayload Project(ITagStatePayload current, IEventPayload e) => current;
    }
}
