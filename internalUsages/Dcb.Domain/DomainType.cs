using Sekiban.Dcb;
using Dcb.Domain.Student;
using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;

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
            types.TagProjectorTypes.RegisterProjector<StudentProjector>();
            types.TagProjectorTypes.RegisterProjector<ClassRoomProjector>();
            
            // Register tag state payload types
            types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
            types.TagStatePayloadTypes.RegisterPayloadType<AvailableClassRoomState>();
            types.TagStatePayloadTypes.RegisterPayloadType<FilledClassRoomState>();
        });
    }
}