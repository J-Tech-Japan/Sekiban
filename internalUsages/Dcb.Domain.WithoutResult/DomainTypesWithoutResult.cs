using Sekiban.Dcb;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;

namespace Dcb.Domain.WithoutResult;

public static class DomainTypesWithoutResult
{
    public static DcbDomainTypes Create()
    {
        return DcbDomainTypes.Simple(types =>
        {
            types.EventTypes.RegisterEventType<global::Dcb.Domain.WithoutResult.Student.StudentCreated>();
            types.EventTypes.RegisterEventType<global::Dcb.Domain.WithoutResult.ClassRoom.ClassRoomCreated>();
            types.EventTypes.RegisterEventType<global::Dcb.Domain.WithoutResult.Enrollment.StudentEnrolledInClassRoom>();
            types.EventTypes.RegisterEventType<global::Dcb.Domain.WithoutResult.Enrollment.StudentDroppedFromClassRoom>();
            types.EventTypes.RegisterEventType<global::Dcb.Domain.WithoutResult.Weather.WeatherForecastCreated>();
            types.EventTypes.RegisterEventType<global::Dcb.Domain.WithoutResult.Weather.WeatherForecastUpdated>();
            types.EventTypes.RegisterEventType<global::Dcb.Domain.WithoutResult.Weather.WeatherForecastDeleted>();
            types.EventTypes.RegisterEventType<global::Dcb.Domain.WithoutResult.Weather.LocationNameChanged>();

            types.TagProjectorTypes.RegisterProjector<global::Dcb.Domain.WithoutResult.Student.StudentProjector>();
            types.TagProjectorTypes.RegisterProjector<global::Dcb.Domain.WithoutResult.ClassRoom.ClassRoomProjector>();
            types.TagProjectorTypes.RegisterProjector<global::Dcb.Domain.WithoutResult.Weather.WeatherForecastProjector>();

            types.TagStatePayloadTypes.RegisterPayloadType<global::Dcb.Domain.WithoutResult.Student.StudentState>();
            types.TagStatePayloadTypes.RegisterPayloadType<global::Dcb.Domain.WithoutResult.ClassRoom.AvailableClassRoomState>();
            types.TagStatePayloadTypes.RegisterPayloadType<global::Dcb.Domain.WithoutResult.ClassRoom.FilledClassRoomState>();
            types.TagStatePayloadTypes.RegisterPayloadType<global::Dcb.Domain.WithoutResult.Weather.WeatherForecastState>();

            types.TagTypes.RegisterTagGroupType<global::Dcb.Domain.WithoutResult.Student.StudentTag>();
            types.TagTypes.RegisterTagGroupType<global::Dcb.Domain.WithoutResult.ClassRoom.ClassRoomTag>();
            types.TagTypes.RegisterTagGroupType<global::Dcb.Domain.WithoutResult.Weather.WeatherForecastTag>();

            types.MultiProjectorTypes.RegisterProjector<global::Dcb.Domain.WithoutResult.Projections.WeatherForecastProjection>();
            types.MultiProjectorTypes.RegisterProjectorWithCustomSerialization<global::Dcb.Domain.WithoutResult.Projections.WeatherForecastProjectorWithTagStateProjector>();
            types.MultiProjectorTypes.RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<global::Dcb.Domain.WithoutResult.Weather.WeatherForecastProjector, global::Dcb.Domain.WithoutResult.Weather.WeatherForecastTag>>();
            types.MultiProjectorTypes.RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<global::Dcb.Domain.WithoutResult.Student.StudentProjector, global::Dcb.Domain.WithoutResult.Student.StudentTag>>();
            types.MultiProjectorTypes.RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<global::Dcb.Domain.WithoutResult.ClassRoom.ClassRoomProjector, global::Dcb.Domain.WithoutResult.ClassRoom.ClassRoomTag>>();

            types.QueryTypes.RegisterListQuery<global::Dcb.Domain.WithoutResult.Queries.GetWeatherForecastListQuery>();
            types.QueryTypes.RegisterListQuery<global::Dcb.Domain.WithoutResult.Queries.GetWeatherForecastListSingleQuery>();
            types.QueryTypes.RegisterListQuery<global::Dcb.Domain.WithoutResult.Queries.GetWeatherForecastListGenericQuery>();
            types.QueryTypes.RegisterListQuery<global::Dcb.Domain.WithoutResult.Queries.GetStudentListQuery>();
            types.QueryTypes.RegisterListQuery<global::Dcb.Domain.WithoutResult.Queries.GetClassRoomListQuery>();

            types.QueryTypes.RegisterQuery<global::Dcb.Domain.WithoutResult.Queries.GetWeatherForecastCountQuery>();
            types.QueryTypes.RegisterQuery<global::Dcb.Domain.WithoutResult.Queries.GetWeatherForecastCountSingleQuery>();
            types.QueryTypes.RegisterQuery<global::Dcb.Domain.WithoutResult.Queries.GetWeatherForecastCountGenericQuery>();
        });
    }

    public static InMemoryDcbExecutorWithoutResult CreateExecutor() => new(Create());
}
