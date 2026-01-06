using Dcb.Domain.Decider.ClassRoom;
using Dcb.Domain.Decider.Projections;
using Dcb.Domain.Decider.Queries;
using Dcb.Domain.Decider.Student;
using Dcb.Domain.Decider.Weather;
using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.Events.Student;
using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.States.Weather;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.MultiProjections;
namespace Dcb.Domain.Decider;

/// <summary>
///     Static class that provides the domain types configuration for Decider pattern
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
            // Register event types (from ImmutableModels)
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

            // Register tag state payload types (from ImmutableModels)
            types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
            types.TagStatePayloadTypes.RegisterPayloadType<AvailableClassRoomState>();
            types.TagStatePayloadTypes.RegisterPayloadType<FilledClassRoomState>();
            types.TagStatePayloadTypes.RegisterPayloadType<WeatherForecastState>();

            // Register tag types (from ImmutableModels)
            types.TagTypes.RegisterTagGroupType<StudentTag>();
            types.TagTypes.RegisterTagGroupType<ClassRoomTag>();
            types.TagTypes.RegisterTagGroupType<WeatherForecastTag>();

            // Register multi-projectors
            types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjection>();

            // Register projectors with custom serialization (for SafeUnsafeProjectionState)
            types.MultiProjectorTypes.RegisterProjectorWithCustomSerialization<WeatherForecastProjectorWithTagStateProjector>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<StudentProjector, StudentTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>>();

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
