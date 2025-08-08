using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Pure.Events;
using Sekiban.Pure.Documents;

namespace DaprSample.Api;

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