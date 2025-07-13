using Dapr;
using Microsoft.AspNetCore.Mvc;
using Dapr.Actors;
using Dapr.Actors.Client;
using Sekiban.Pure.Dapr;
using Sekiban.Pure.Dapr.Events;
using Sekiban.Pure;

namespace DaprSample.Api.Controllers;

/// <summary>
/// Controller to handle Dapr pub/sub events and forward them to MultiProjectorActors
/// </summary>
[ApiController]
[Route("pubsub")]
public class EventPubSubController : ControllerBase
{
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly ILogger<EventPubSubController> _logger;

    public EventPubSubController(
        IActorProxyFactory actorProxyFactory,
        SekibanDomainTypes domainTypes,
        ILogger<EventPubSubController> logger)
    {
        _actorProxyFactory = actorProxyFactory;
        _domainTypes = domainTypes;
        _logger = logger;
    }

    /// <summary>
    /// Handle all events from the events.all topic
    /// </summary>
    [Topic("sekiban-pubsub", "events.all")]
    [HttpPost("events")]
    public async Task<IActionResult> HandleEvent([FromBody] DaprEventEnvelope envelope)
    {
        _logger.LogInformation("Received event from pub/sub: {EventType} for aggregate {AggregateId}", 
            envelope.EventType, envelope.AggregateId);

        try
        {
            // Get all multi-projector names
            var projectorNames = _domainTypes.MultiProjectorsType.GetAllProjectorNames();
            
            _logger.LogInformation("Forwarding event to {Count} multi-projectors: {Projectors}", 
                projectorNames.Count(), string.Join(", ", projectorNames));

            // Forward the event to each multi-projector actor
            var tasks = projectorNames.Select(async projectorName =>
            {
                try
                {
                    var actorId = new ActorId($"aggregatelistprojector-{projectorName.ToLower()}");
                    var actor = _actorProxyFactory.CreateActorProxy<Sekiban.Pure.Dapr.Actors.IMultiProjectorActor>(
                        actorId, 
                        nameof(Sekiban.Pure.Dapr.Actors.MultiProjectorActor));
                    
                    await actor.HandlePublishedEvent(envelope);
                    
                    _logger.LogDebug("Successfully forwarded event to {ProjectorName}", projectorName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to forward event to multi-projector {ProjectorName}", projectorName);
                    // Don't fail the whole operation if one projector fails
                }
            });
            
            await Task.WhenAll(tasks);
            
            _logger.LogInformation("Event forwarding completed");
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling pub/sub event");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Handle specific event types
    /// </summary>
    [Topic("sekiban-pubsub", "events.*")]
    [HttpPost("events/{eventType}")]
    public Task<IActionResult> HandleSpecificEvent(string eventType, [FromBody] DaprEventEnvelope envelope)
    {
        _logger.LogInformation("Received specific event type {EventType} from pub/sub", eventType);
        return HandleEvent(envelope);
    }
}