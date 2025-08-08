using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

public class EventPublishingTests
{
    private readonly InMemoryObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;
    private readonly GeneralSekibanExecutor _executor;
    private readonly InMemoryEventPublisher _publisher;
    private readonly InMemorySekibanStream _stream;

    public EventPublishingTests()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
        _actorAccessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _stream = new InMemorySekibanStream("test.topic");
        var resolver = new InMemoryStreamDestinationResolver(_stream);
        _publisher = new InMemoryEventPublisher(resolver);
        _executor = new GeneralSekibanExecutor(_eventStore, _actorAccessor, _domainTypes, _publisher);
    }

    [Fact]
    public async Task Executor_Should_Publish_Event_To_Configured_Topic()
    {
        var cmd = new TestPublishCommand("Hello");
        var handler = new TestPublishHandler();
        var result = await _executor.ExecuteAsync(cmd, handler);
        Assert.True(result.IsSuccess);

        var attempts = 0;
        while (attempts < 10 && _publisher.Published.Count == 0)
        {
            await Task.Delay(10);
            attempts++;
        }

        Assert.Single(_publisher.Published);
        var published = _publisher.Published.First();
        Assert.Equal("test.topic", published.Topic);
        Assert.Equal("TestPublishEvent", published.Event.Payload.GetType().Name);
    }

    [Fact]
    public async Task Executor_Should_Publish_Multiple_Events()
    {
        var handler = new MultiEventHandler();
        var cmd = new TestPublishCommand("Batch");
        var result = await _executor.ExecuteAsync(cmd, handler);
        Assert.True(result.IsSuccess);

        var attempts = 0;
        while (attempts < 10 && _publisher.Published.Count < 2)
        {
            await Task.Delay(10);
            attempts++;
        }

        Assert.Equal(2, _publisher.Published.Count);
    }

    private record TestPublishCommand(string Name) : ICommand;
    private record TestPublishEvent(string Name) : IEventPayload;
    private record TestPublishTag : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTagGroup() => "PubTest";
        public string GetTagContent() => "One";
    }

    private class TestPublishHandler : ICommandHandler<TestPublishCommand>
    {
        public Task<ResultBox<EventOrNone>> HandleAsync(TestPublishCommand command, ICommandContext context)
        {
            var tag = new TestPublishTag();
            var evt = new TestPublishEvent(command.Name);
            return Task.FromResult(EventOrNone.Event(evt, tag));
        }
    }

    private class MultiEventHandler : ICommandHandler<TestPublishCommand>
    {
        public Task<ResultBox<EventOrNone>> HandleAsync(TestPublishCommand command, ICommandContext context)
        {
            var tag = new TestPublishTag();
            var evt1 = new TestPublishEvent(command.Name + "1");
            var evt2 = new TestPublishEvent(command.Name + "2");

            // Append first event via context to simulate multi-event production
            context.AppendEvent(new EventPayloadWithTags(evt1, new List<ITag> { tag }));

            // Return second event as the handler result
            return Task.FromResult(EventOrNone.Event(evt2, tag));
        }
    }
}
