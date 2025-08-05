using System.Text.Json;
using Sekiban.Dcb;
using Sekiban.Dcb.Domains;

namespace Dcb.Domain;

/// <summary>
/// Static class that provides the domain types configuration
/// </summary>
public static class DomainType
{
    /// <summary>
    /// Gets the configured DcbDomainTypes for this domain
    /// </summary>
    public static DcbDomainTypes GetDomainTypes()
    {
        // Create instances of the domain type managers
        var eventTypes = new SimpleEventTypes();
        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        
        // Register domain-specific tag state payload types
        tagStatePayloadTypes.RegisterPayloadType<StudentState>(nameof(StudentState));
        tagStatePayloadTypes.RegisterPayloadType<AvailableClassRoomState>(nameof(AvailableClassRoomState));
        tagStatePayloadTypes.RegisterPayloadType<FilledClassRoomState>(nameof(FilledClassRoomState));
        
        // Register tag projectors
        tagProjectorTypes.RegisterProjector(new StudentProjector());
        tagProjectorTypes.RegisterProjector(new ClassRoomProjector());
        
        // Note: Command handlers are no longer registered here
        // Commands either implement ICommandWithHandler or are passed with their handlers to ICommandExecutor
        
        // Configure JSON serialization options
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        
        return new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            jsonOptions
        );
    }
}