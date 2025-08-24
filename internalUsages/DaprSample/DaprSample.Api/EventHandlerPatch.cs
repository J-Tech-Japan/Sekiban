using Sekiban.Pure.Events;
namespace DaprSample.Api;

/// <summary>
///     Temporary patch to make the event handler work with empty events
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
