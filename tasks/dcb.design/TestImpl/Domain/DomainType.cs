using System.Text.Json;
using DcbLib;
using DcbLib.Domains;

namespace Domain;

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
        var commandTypes = new SimpleCommandTypes();
        
        // Register tag projectors
        tagProjectorTypes.RegisterProjector(new StudentProjector());
        tagProjectorTypes.RegisterProjector(new ClassRoomProjector());
        
        // Register command handlers
        commandTypes.RegisterHandler<CreateStudent>(new CreateStudentHandler());
        commandTypes.RegisterHandler<CreateClassRoom>(new CreateClassRoomHandler());
        commandTypes.RegisterHandler<EnrollStudentInClassRoom>(new EnrollStudentInClassRoomHandler());
        commandTypes.RegisterHandler<DropStudentFromClassRoom>(new DropStudentFromClassRoomHandler());
        
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
            commandTypes,
            jsonOptions
        );
    }
}