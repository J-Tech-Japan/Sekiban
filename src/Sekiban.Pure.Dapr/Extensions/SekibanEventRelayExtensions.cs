using Dapr;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure;

namespace Sekiban.Pure.Dapr.Extensions;

/// <summary>
/// Provides PubSub event relay with MinimalAPI extension methods
/// Must be explicitly enabled on the client side (opt-in approach)
/// </summary>
public static class SekibanEventRelayExtensions
{
    /// <summary>
    /// Add Sekiban event relay endpoint as MinimalAPI
    /// </summary>
    /// <param name="app">IEndpointRouteBuilder</param>
    /// <param name="options">PubSub relay options</param>
    /// <returns>RouteHandlerBuilder</returns>
    public static RouteHandlerBuilder MapSekibanEventRelay(
        this IEndpointRouteBuilder app,
        SekibanPubSubRelayOptions? options = null)
    {
        options ??= new SekibanPubSubRelayOptions();

        var builder = app.MapPost(options.EndpointPath,
            async (
                DaprEventEnvelope envelope,
                [FromServices]IActorProxyFactory actorProxyFactory,
                [FromServices]SekibanDomainTypes domainTypes,
                [FromServices]ILogger<SekibanEventRelayHandler> logger) =>
            {
                return await HandleEventAsync(envelope, actorProxyFactory, domainTypes, logger, options);
            })
            .WithTopic(options.PubSubName, options.TopicName)
            .WithName("SekibanEventRelay")
            .WithMetadata("Tags", new[] { "Internal" })
            .WithMetadata("Summary", "Sekiban PubSub Event Relay")
            .WithMetadata("Description", "Internal endpoint for relaying Dapr PubSub events to MultiProjectorActors")
            .Produces<object>(200)
            .Produces<ProblemDetails>(500);

        if (!string.IsNullOrEmpty(options.ConsumerGroup))
        {
            builder.WithMetadata("dapr.io/consumer-group", options.ConsumerGroup);
        }

        return builder;
    }

    /// <summary>
    /// Add Sekiban event relay endpoint that supports multiple topics
    /// </summary>
    /// <param name="app">IEndpointRouteBuilder</param>
    /// <param name="topicConfigs">List of topic configurations</param>
    /// <returns>List of RouteHandlerBuilder</returns>
    public static List<RouteHandlerBuilder> MapSekibanEventRelayMultiTopic(
        this IEndpointRouteBuilder app,
        params SekibanPubSubRelayOptions[] topicConfigs)
    {
        var builders = new List<RouteHandlerBuilder>();
        
        foreach (var config in topicConfigs)
        {
            var builder = app.MapSekibanEventRelay(config);
            builders.Add(builder);
        }

        return builders;
    }

    /// <summary>
    /// Add Sekiban event relay based on configuration
    /// </summary>
    /// <param name="app">IEndpointRouteBuilder</param>
    /// <param name="configure">Configuration action</param>
    /// <returns>RouteHandlerBuilder or null</returns>
    public static RouteHandlerBuilder? MapSekibanEventRelayIfEnabled(
        this IEndpointRouteBuilder app,
        Action<SekibanPubSubRelayOptions>? configure = null)
    {
        var options = new SekibanPubSubRelayOptions();
        configure?.Invoke(options);

        if (!options.Enabled)
        {
            return null;
        }

        return app.MapSekibanEventRelay(options);
    }

    /// <summary>
    /// Add Sekiban event relay only in development environment
    /// </summary>
    /// <param name="app">IEndpointRouteBuilder</param>
    /// <param name="isDevelopment">Whether it's development environment</param>
    /// <param name="options">PubSub relay options</param>
    /// <returns>RouteHandlerBuilder or null</returns>
    public static RouteHandlerBuilder? MapSekibanEventRelayForDevelopment(
        this IEndpointRouteBuilder app,
        bool isDevelopment,
        SekibanPubSubRelayOptions? options = null)
    {
        if (!isDevelopment)
        {
            return null;
        }

        return app.MapSekibanEventRelay(options);
    }

    /// <summary>
    /// Actual logic for event processing
    /// </summary>
    private static async Task<IResult> HandleEventAsync(
        DaprEventEnvelope envelope,
        IActorProxyFactory actorProxyFactory,
        SekibanDomainTypes domainTypes,
        ILogger<SekibanEventRelayHandler> logger,
        SekibanPubSubRelayOptions options)
    {
        try
        {
            logger.LogDebug("Received event envelope: AggregateId={AggregateId}, Version={Version}, Endpoint={Endpoint}",
                envelope.AggregateId, envelope.Version, options.EndpointPath);

            // Get all multi-projector names that should process this event
            var projectorNames = domainTypes.MultiProjectorsType.GetAllProjectorNames();

            if (!projectorNames.Any())
            {
                logger.LogDebug("No projectors found to process event {EventId}", envelope.EventId);
                return Results.Ok(new { Message = "No projectors to process", EventId = envelope.EventId });
            }

            // Forward the event to each multi-projector actor
            var tasks = projectorNames.Select(async projectorName =>
            {
                try
                {
                    var actorId = new ActorId(projectorName);
                    var actor = actorProxyFactory.CreateActorProxy<IMultiProjectorActor>(
                        actorId,
                        nameof(MultiProjectorActor));

                    await actor.HandlePublishedEvent(envelope);
                    logger.LogDebug("Forwarded event to projector: {ProjectorName}", projectorName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to forward event to projector: {ProjectorName}", projectorName);
                    
                    if (!options.ContinueOnProjectorFailure)
                    {
                        throw;
                    }
                }
            });

            await Task.WhenAll(tasks);

            logger.LogDebug("Successfully processed event {EventId} for {ProjectorCount} projectors", 
                envelope.EventId, projectorNames.Count());

            return Results.Ok(new { Message = "Event processed successfully", EventId = envelope.EventId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling event from PubSub: EventId={EventId}", envelope.EventId);
            return Results.Problem(
                title: "Event processing failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }
}

/// <summary>
/// Sekiban event relay option settings
/// </summary>
public class SekibanPubSubRelayOptions
{
    /// <summary>
    /// Whether to enable relay functionality
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// PubSub component name
    /// </summary>
    public string PubSubName { get; set; } = "sekiban-pubsub";

    /// <summary>
    /// Topic name to subscribe
    /// </summary>
    public string TopicName { get; set; } = "events.all";

    /// <summary>
    /// Endpoint path
    /// </summary>
    public string EndpointPath { get; set; } = "/internal/pubsub/events";

    /// <summary>
    /// Whether to continue processing on individual projector failures
    /// </summary>
    public bool ContinueOnProjectorFailure { get; set; } = true;

    /// <summary>
    /// Consumer Group name (supported in Dapr 1.14+)
    /// Instances in the same Consumer Group will only have one instance receive events to avoid duplicate processing
    /// </summary>
    public string? ConsumerGroup { get; set; }

    /// <summary>
    /// Maximum concurrency this relay processes
    /// </summary>
    public int MaxConcurrency { get; set; } = 10;

    /// <summary>
    /// Whether to enable dead letter queue
    /// </summary>
    public bool EnableDeadLetterQueue { get; set; } = false;

    /// <summary>
    /// Dead letter queue topic name
    /// </summary>
    public string DeadLetterTopic { get; set; } = "events.dead-letter";

    /// <summary>
    /// Maximum retry count
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;
}

/// <summary>
/// Marker class for logging
/// </summary>
internal class SekibanEventRelayHandler
{
}
