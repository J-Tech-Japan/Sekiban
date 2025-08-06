using Sekiban.Dcb;

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
        return DcbDomainTypes.Simple(types =>
        {
            // Register event types
            types.EventTypes.RegisterEventType<StudentCreated>();
            types.EventTypes.RegisterEventType<ClassRoomCreated>();
            types.EventTypes.RegisterEventType<StudentEnrolledInClassRoom>();
            types.EventTypes.RegisterEventType<StudentDroppedFromClassRoom>();
            
            // Register tag projectors
            types.TagProjectorTypes.RegisterProjector(new StudentProjector());
            types.TagProjectorTypes.RegisterProjector(new ClassRoomProjector());
            
            // Register tag state payload types
            types.TagStatePayloadTypes.RegisterPayloadType<StudentState>(nameof(StudentState));
            types.TagStatePayloadTypes.RegisterPayloadType<AvailableClassRoomState>(nameof(AvailableClassRoomState));
            types.TagStatePayloadTypes.RegisterPayloadType<FilledClassRoomState>(nameof(FilledClassRoomState));
        });
    }
}