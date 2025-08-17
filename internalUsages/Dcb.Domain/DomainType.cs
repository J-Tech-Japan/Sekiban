using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Projections;
using Dcb.Domain.Student;
using Dcb.Domain.Weather;
using Sekiban.Dcb;
using Sekiban.Dcb.MultiProjections;
namespace Dcb.Domain;

/// <summary>
///     Static class that provides the domain types configuration
/// </summary>
public static class DomainType
{
    /// <summary>
    ///     Gets the configured DcbDomainTypes for this domain
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
            types.EventTypes.RegisterEventType<WeatherForecastCreated>();
            types.EventTypes.RegisterEventType<WeatherForecastUpdated>();
            types.EventTypes.RegisterEventType<WeatherForecastDeleted>();

            // Register tag projectors
            types.TagProjectorTypes.RegisterProjector<StudentProjector>();
            types.TagProjectorTypes.RegisterProjector<ClassRoomProjector>();
            types.TagProjectorTypes.RegisterProjector<WeatherForecastProjector>();

            // Register tag state payload types
            types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
            types.TagStatePayloadTypes.RegisterPayloadType<AvailableClassRoomState>();
            types.TagStatePayloadTypes.RegisterPayloadType<FilledClassRoomState>();
            types.TagStatePayloadTypes.RegisterPayloadType<WeatherForecastState>();

            // Register tag types
            types.TagTypes.RegisterTagGroupType<StudentTag>();
            types.TagTypes.RegisterTagGroupType<ClassRoomTag>();
            types.TagTypes.RegisterTagGroupType<WeatherForecastTag>();

            // Register multi-projectors
            types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjection>();
            types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjectorWithTagStateProjector>();
            types.MultiProjectorTypes.RegisterProjector<GenericTagMultiProjector<WeatherForecastProjector>>();
            
            // Register V6 projectors for testing
            types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjectionV6>();
            types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjectorWithTagStateProjectorV6>();
            types.MultiProjectorTypes.RegisterProjector<GenericTagMultiProjectorV6<WeatherForecastProjector>>();
        });
    }
}
