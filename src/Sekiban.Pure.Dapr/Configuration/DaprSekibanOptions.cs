namespace Sekiban.Pure.Dapr.Configuration;

public class DaprSekibanOptions
{
    public string StateStoreName { get; set; } = "sekiban-eventstore";
    public string PubSubName { get; set; } = "sekiban-pubsub";
    public string EventTopicName { get; set; } = "domain-events";
    
    public string QueryServiceAppId { get; set; } = "sekiban-query-service";
    public TimeSpan ActorIdleTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan ActorScanInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxConcurrentActors { get; set; } = 100;
}