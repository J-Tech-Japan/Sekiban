using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Pure.Events;
using Sekiban.Pure.Documents;
using ResultBoxes;

namespace DaprSekiban.ApiService;

/// <summary>
/// Temporary patch to make the event handler work with empty events
/// </summary>
public static class EventHandlerPatch
{
    public static IServiceCollection AddEventHandlerPatch(this IServiceCollection services)
    {
        // Override the IEventReader to return empty events initially
        services.AddSingleton<IEventReader, PatchedEventReader>();
        return services;
    }
}

public class PatchedEventReader : IEventReader
{
    private readonly ILogger<PatchedEventReader> _logger;
    
    public PatchedEventReader(ILogger<PatchedEventReader> logger)
    {
        _logger = logger;
    }
    
    public Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo retrievalInfo)
    {
        _logger.LogInformation("PatchedEventReader.GetEvents called");
        
        // Always return empty list to avoid timeout
        return Task.FromResult(ResultBox<IReadOnlyList<IEvent>>.FromValue(
            (IReadOnlyList<IEvent>)new List<IEvent>()));
    }
}