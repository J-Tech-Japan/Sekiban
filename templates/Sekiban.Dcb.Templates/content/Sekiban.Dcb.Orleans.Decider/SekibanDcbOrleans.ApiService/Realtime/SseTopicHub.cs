using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SekibanDcbOrleans.ApiService.Realtime;

public record SseStreamUpdate(
    string EventType,
    string SortableUniqueId,
    Guid EventId);

public sealed class SseSubscription : IDisposable
{
    private readonly Action _onDispose;
    private bool _disposed;

    public SseSubscription(Guid id, ChannelReader<SseStreamUpdate> reader, Action onDispose)
    {
        Id = id;
        Reader = reader;
        _onDispose = onDispose;
    }

    public Guid Id { get; }
    public ChannelReader<SseStreamUpdate> Reader { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _onDispose();
    }
}

public sealed class SseTopicHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<SseStreamUpdate>>> _topics =
        new(StringComparer.Ordinal);

    public SseSubscription Subscribe(string topic, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SseStreamUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var id = Guid.NewGuid();
        var subscribers = _topics.GetOrAdd(topic, _ => new ConcurrentDictionary<Guid, Channel<SseStreamUpdate>>());
        subscribers[id] = channel;

        void Remove()
        {
            if (_topics.TryGetValue(topic, out var current) && current.TryRemove(id, out var removed))
            {
                removed.Writer.TryComplete();
            }
        }

        cancellationToken.Register(Remove);
        return new SseSubscription(id, channel.Reader, Remove);
    }

    public void Publish(string topic, SseStreamUpdate update)
    {
        if (!_topics.TryGetValue(topic, out var subscribers)) return;

        foreach (var channel in subscribers.Values)
        {
            channel.Writer.TryWrite(update);
        }
    }
}
