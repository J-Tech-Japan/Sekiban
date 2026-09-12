using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans;
using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Orleans.Streams;
using Sekiban.Dcb.Subscriptions;

namespace Sekiban.Dcb.Orleans.Subscriptions;

/// <summary>Registers Orleans as a data-free wake source for durable store-driven subscribers.</summary>
public static class OrleansDurableSubscriptionServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanDcbOrleansDurableSubscriptionNudges(
        this IServiceCollection services)
    {
        services.AddSingleton<IDurableSubscriptionNudgeFactory, OrleansDurableSubscriptionNudgeFactory>();
        return services;
    }
}

internal sealed class OrleansDurableSubscriptionNudgeFactory : IDurableSubscriptionNudgeFactory
{
    private readonly IClusterClient _clusterClient;
    private readonly IEventSubscriptionResolver _resolver;

    public OrleansDurableSubscriptionNudgeFactory(
        IClusterClient clusterClient,
        IEventSubscriptionResolver resolver)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public IDurableSubscriptionNudge Create(
        DurableSubscriptionIdentity identity,
        Func<ValueTask> onNudge)
    {
        ArgumentNullException.ThrowIfNull(onNudge);
        var stream = _resolver.Resolve(ServiceIdGrainKey.Build(identity.ServiceId, identity.Name)) as OrleansSekibanStream;
        if (stream is null)
        {
            throw new InvalidOperationException("The durable subscription resolver did not return an Orleans stream.");
        }

        var provider = _clusterClient.GetStreamProvider(stream.ProviderName);
        var asyncStream = provider.GetStream<SerializableEvent>(StreamId.Create(stream.StreamNamespace, stream.StreamId));
        return OrleansDurableSubscriptionNudge.CreateAsync(asyncStream, onNudge).GetAwaiter().GetResult();
    }
}

internal sealed class OrleansDurableSubscriptionNudge : IDurableSubscriptionNudge
{
    private readonly StreamSubscriptionHandle<SerializableEvent> _handle;

    private OrleansDurableSubscriptionNudge(StreamSubscriptionHandle<SerializableEvent> handle) => _handle = handle;

    internal static async Task<OrleansDurableSubscriptionNudge> CreateAsync(
        IAsyncStream<SerializableEvent> stream,
        Func<ValueTask> onNudge)
    {
        var observer = new NudgeObserver(onNudge);
        var handle = await stream.SubscribeAsync(observer, null).ConfigureAwait(false);
        return new OrleansDurableSubscriptionNudge(handle);
    }

    public async ValueTask DisposeAsync() => await _handle.UnsubscribeAsync().ConfigureAwait(false);

    private sealed class NudgeObserver : IAsyncObserver<SerializableEvent>
    {
        private readonly Func<ValueTask> _onNudge;

        internal NudgeObserver(Func<ValueTask> onNudge) => _onNudge = onNudge;

        public Task OnNextAsync(SerializableEvent item, StreamSequenceToken? token = null) =>
            _onNudge().AsTask();

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex) => _onNudge().AsTask();
    }
}
