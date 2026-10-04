using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Student;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;

namespace Sekiban.Dcb.WithoutResult.Tests;

public sealed class ObservedTagPositionTests
{
    private sealed record Command(Guid Id) : ICommand;
    private sealed class ObservedStore(Sekiban.Dcb.Domains.IEventTypes eventTypes, bool enabled)
        : Sekiban.Dcb.Testing.InMemoryEventStore(eventTypes), IObservedTagPositionEventStore
    {
        public bool RecordsObservedTagPositions => enabled;
        public IReadOnlyDictionary<string, string?>? Observations;
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsWithObservedTagPositionsAsync(IEnumerable<SerializableEvent> events,
                IReadOnlyDictionary<string, string?> observations, CancellationToken cancellationToken = default)
        {
            Observations = observations;
            return WriteSerializableEventsAsync(events);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ExceptionFacade_UsesOptionalRecordingOnTypedWrite(bool enabled)
    {
        var domain = DomainType.GetDomainTypes();
        var store = new ObservedStore(domain.EventTypes, enabled);
        var executor = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, domain), domain);
        var command = new Command(Guid.NewGuid());
        var result = await executor.ExecuteAsync(command, (Command cmd, ICommandContext ctx) =>
            ctx.AppendEvent(new StudentCreated(cmd.Id, "observed", 5), new StudentTag(cmd.Id)));
        Assert.Single(result.Events);
        if (enabled) Assert.Null(Assert.Single(store.Observations!).Value);
        else Assert.Null(store.Observations);
        Assert.Equal(1, (await store.GetEventCountAsync()).GetValue());
    }

    [Fact]
    public void CoreRecordingContract_IsAdditiveAndSerializableOnly()
    {
        var contract = typeof(IObservedTagPositionEventStore);
        Assert.Equal(typeof(bool), contract.GetProperty(nameof(IObservedTagPositionEventStore.RecordsObservedTagPositions))!.PropertyType);
        var method = Assert.Single(contract.GetMethods().Where(m => !m.IsSpecialName));
        Assert.Equal(nameof(IObservedTagPositionEventStore.WriteSerializableEventsWithObservedTagPositionsAsync), method.Name);
        Assert.Equal(typeof(IEventStore).GetMethod(nameof(IEventStore.WriteSerializableEventsAsync))!.ReturnType, method.ReturnType);
        Assert.Equal(new[] { typeof(IEnumerable<SerializableEvent>), typeof(IReadOnlyDictionary<string, string?>), typeof(CancellationToken) },
            method.GetParameters().Select(p => p.ParameterType));
        Assert.DoesNotContain(typeof(IEventStore).GetMethods(), m => m.Name.Contains("Observed", StringComparison.Ordinal));
    }
}
