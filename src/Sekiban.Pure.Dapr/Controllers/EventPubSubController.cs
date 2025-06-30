using Dapr;
using Dapr.Actors;
using Dapr.Actors.Client;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Dapr.Extensions;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure;

namespace Sekiban.Pure.Dapr.Controllers;

/// <summary>
/// Controller to handle Dapr PubSub events and forward them to MultiProjectorActors
/// </summary>
[ApiController]
[Route("pubsub")]
public class EventPubSubController : ControllerBase
{
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly DaprSekibanOptions _options;
    private readonly ILogger<EventPubSubController> _logger;

    public EventPubSubController(
        IActorProxyFactory actorProxyFactory,
        SekibanDomainTypes domainTypes,
        IOptions<DaprSekibanOptions> options,
        ILogger<EventPubSubController> logger)
    {
        _actorProxyFactory = actorProxyFactory;
        _domainTypes = domainTypes;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Handle all domain events from PubSub
    /// </summary>
    [Topic("sekiban-pubsub", "events.all")]
    [HttpPost("events")]
    public async Task<IActionResult> HandleEvent([FromBody] DaprEventEnvelope envelope)
    {
        try
        {
            _logger.LogDebug("Received event envelope: AggregateId={AggregateId}, Version={Version}", 
                envelope.AggregateId, envelope.Version);

            // Get all multi-projector names that should process this event
            var projectorNames = _domainTypes.MultiProjectorsType.GetAllProjectorNames();

            // Forward the event to each multi-projector actor
            var tasks = projectorNames.Select(async projectorName =>
            {
                try
                {
                    var actorId = new ActorId(projectorName);
                    var actor = _actorProxyFactory.CreateActorProxy<IMultiProjectorActor>(
                        actorId, 
                        nameof(MultiProjectorActor));
                    
                    await actor.HandlePublishedEvent(envelope);
                    _logger.LogDebug("Forwarded event to projector: {ProjectorName}", projectorName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to forward event to projector: {ProjectorName}", projectorName);
                }
            });

            await Task.WhenAll(tasks);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling event from PubSub");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Handle specific event types from PubSub
    /// This allows for more granular subscriptions if needed
    /// </summary>
    [Topic("sekiban-pubsub", "events.*")]
    [HttpPost("events/{eventType}")]
    public async Task<IActionResult> HandleSpecificEvent(string eventType, [FromBody] DaprEventEnvelope envelope)
    {
        // This delegates to the same handler as the general event handler
        return await HandleEvent(envelope);
    }
}