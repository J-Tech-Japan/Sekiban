using Dapr.Client;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Repositories;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.Services;

// Simplified repository that uses Dapr state store for persistence
public class DaprEventStore : Repository
{
    private readonly DaprClient _daprClient;
    private readonly DaprSekibanOptions _options;

    public DaprEventStore(
        DaprClient daprClient,
        IOptions<DaprSekibanOptions> options)
    {
        _daprClient = daprClient;
        _options = options.Value;
        
        // Set up serialization for the base Repository
        Serializer = @event => JsonSerializer.Serialize(@event);
        Deserializer = json => JsonSerializer.Deserialize<IEvent>(json) ?? throw new InvalidOperationException("Failed to deserialize event");
    }

    // Override Save to persist to Dapr state store
    public new async Task<ResultBox<List<IEvent>>> Save(List<IEvent> events)
    {
        var result = base.Save(events);
        if (!result.IsSuccess) return result;
        
        // Also persist to Dapr state store
        // This is a simplified implementation - in production you'd want proper event sourcing
        try
        {
            var allEvents = Events.ToList();
            await _daprClient.SaveStateAsync(
                _options.StateStoreName,
                "all-events",
                allEvents);
                
            // Publish events
            foreach (var @event in events)
            {
                await _daprClient.PublishEventAsync(
                    _options.PubSubName,
                    _options.EventTopicName,
                    @event);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            return ResultBox<List<IEvent>>.FromException(ex);
        }
    }
}