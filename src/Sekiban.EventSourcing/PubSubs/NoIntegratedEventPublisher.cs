using Sekiban.EventSourcing.IntegratedEvents;
namespace Sekiban.EventSourcing.PubSubs;

public class NoIntegratedEventPublisher : IIntegratedEventPublisher
{
    public async Task SaveAndPublishAsync<TEvent>(TEvent ev) where TEvent : IntegratedEvent
    {
        // 何もしない。Event Grid や Event Bus で実際にパブリッシュするコードは、IIntegratedEventPublisherを継承させて別に定義する。
        await Task.CompletedTask;
    }
}
