using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.InMemory;

public class InMemoryEventPublisher : IEventPublisher
{
    private readonly ConcurrentBag<(string Topic, Event Event, IReadOnlyCollection<ITag> Tags)> _published = new();
    private readonly IStreamDestinationResolver _resolver;

    public IReadOnlyCollection<(string Topic, Event Event, IReadOnlyCollection<ITag> Tags)> Published =>
        _published.ToArray();

    public InMemoryEventPublisher(IStreamDestinationResolver resolver) => _resolver = resolver;

    public Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
        CancellationToken cancellationToken = default)
    {
        foreach (var (evt, tags) in events)
        {
            var streams = _resolver.Resolve(evt, tags) ?? Enumerable.Empty<ISekibanStream>();
            foreach (var stream in streams)
            {
                var topic = stream.GetTopic(evt);
                _published.Add((topic, evt, tags));
            }
        }
        return Task.CompletedTask;
    }
}
