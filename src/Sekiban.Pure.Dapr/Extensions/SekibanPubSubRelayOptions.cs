namespace Sekiban.Pure.Dapr.Extensions;

/// <summary>
///     Sekiban event relay option settings
/// </summary>
public class SekibanPubSubRelayOptions
{
    /// <summary>
    ///     Whether to enable relay functionality
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     PubSub component name
    /// </summary>
    public string PubSubName { get; set; } = "sekiban-pubsub";

    /// <summary>
    ///     Topic name to subscribe
    /// </summary>
    public string TopicName { get; set; } = "events.all";

    /// <summary>
    ///     Endpoint path
    /// </summary>
    public string EndpointPath { get; set; } = "/internal/pubsub/events";

    /// <summary>
    ///     Whether to continue processing on individual projector failures
    /// </summary>
    public bool ContinueOnProjectorFailure { get; set; } = true;

    /// <summary>
    ///     Consumer Group name (supported in Dapr 1.14+)
    ///     Instances in the same Consumer Group will only have one instance receive events to avoid duplicate processing
    /// </summary>
    public string? ConsumerGroup { get; set; }

    /// <summary>
    ///     Maximum concurrency this relay processes
    /// </summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>
    ///     Whether to enable dead letter queue
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; } = false;

    /// <summary>
    ///     Dead letter queue topic name
    /// </summary>
    public string DeadLetterTopic { get; set; } = "events.dead-letter";

    /// <summary>
    ///     Maximum retry count
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;
}
