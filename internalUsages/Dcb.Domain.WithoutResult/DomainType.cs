using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Enrollment;
using Dcb.Domain.WithoutResult.Projections;
using Dcb.Domain.WithoutResult.Queries;
using Dcb.Domain.WithoutResult.Student;
using Dcb.Domain.WithoutResult.Weather;
using Sekiban.Dcb;
using Sekiban.Dcb.MultiProjections;
namespace Dcb.Domain.WithoutResult;

/// <summary>
///     Static class that provides the domain types configuration for WithoutResult pattern
/// </summary>
public static class DomainType
{
    /// <summary>
    ///     Gets the configured DcbDomainTypes for this domain
    /// </summary>
    public static DcbDomainTypes GetDomainTypes()
    {
        return DcbDomainTypesExtensions.Simple(types =>
        {
            // Register event types
            types.EventTypes.RegisterEventType<StudentCreated>();
            types.EventTypes.RegisterEventType<ClassRoomCreated>();
            types.EventTypes.RegisterEventType<StudentEnrolledInClassRoom>();
            types.EventTypes.RegisterEventType<StudentDroppedFromClassRoom>();
            types.EventTypes.RegisterEventType<WeatherForecastCreated>();
            types.EventTypes.RegisterEventType<WeatherForecastUpdated>();
            types.EventTypes.RegisterEventType<WeatherForecastDeleted>();
            types.EventTypes.RegisterEventType<LocationNameChanged>();

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

            // Register projectors with custom serialization (for SafeUnsafeProjectionState)
            types.MultiProjectorTypes.RegisterProjectorWithCustomSerializationWithoutResult<WeatherForecastProjectorWithTagStateProjector>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerializationWithoutResult<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerializationWithoutResult<GenericTagMultiProjector<StudentProjector, StudentTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerializationWithoutResult<GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>>();

            // Register list queries
            types.QueryTypes.RegisterListQuery<GetWeatherForecastListQuery>();
            types.QueryTypes.RegisterListQuery<GetWeatherForecastListSingleQuery>();
            types.QueryTypes.RegisterListQuery<GetWeatherForecastListGenericQuery>();
            types.QueryTypes.RegisterListQuery<GetStudentListQuery>();
            types.QueryTypes.RegisterListQuery<GetClassRoomListQuery>();

            // Register regular queries
            types.QueryTypes.RegisterQuery<GetWeatherForecastCountQuery>();
            types.QueryTypes.RegisterQuery<GetWeatherForecastCountSingleQuery>();
            types.QueryTypes.RegisterQuery<GetWeatherForecastCountGenericQuery>();
        });
    }
}
