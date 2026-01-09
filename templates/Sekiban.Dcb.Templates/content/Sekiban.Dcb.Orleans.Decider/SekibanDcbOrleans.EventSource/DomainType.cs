using Dcb.EventSource.ClassRoom;
using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.EventSource.MeetingRoom.Equipment;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.User;
using Dcb.EventSource.Student;
using Dcb.EventSource.Weather;
using Dcb.ImmutableModels;
using Dcb.ImmutableModels.Tags;
using Dcb.MeetingRoomModels;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.MultiProjections;
namespace Dcb.EventSource;

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
            // Auto-register from ImmutableModels assembly
            types.AddAllEventsFromAssembly<ImmutableModelTypes>();
            types.AddAllTagTypesFromAssembly<ImmutableModelTypes>();
            types.AddAllTagStatePayloadsFromAssembly<ImmutableModelTypes>();

            // Auto-register from MeetingRoomModels assembly
            types.AddAllEventsFromAssembly<MeetingRoomModelTypes>();
            types.AddAllTagTypesFromAssembly<MeetingRoomModelTypes>();
            types.AddAllTagStatePayloadsFromAssembly<MeetingRoomModelTypes>();

            // Auto-register from EventSource assembly
            // (includes TagProjectors, MultiProjectors, ListQueries, and Queries)
            types.AddAllTagProjectorsFromAssembly<EventSourceTypes>();
            types.AddAllMultiProjectorsFromAssembly<EventSourceTypes>();
            types.AddAllListQueriesFromAssembly<EventSourceTypes>();
            types.AddAllQueriesFromAssembly<EventSourceTypes>();

            // Register GenericTagMultiProjector instances for ImmutableModels
            // (constructed generic types need manual registration)
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<StudentProjector, StudentTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>>();

            // Register GenericTagMultiProjector instances for MeetingRoomModels
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<RoomProjector, RoomTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<ReservationProjector, ReservationTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<UserMonthlyReservationProjector, UserMonthlyReservationTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<ApprovalRequestProjector, ApprovalRequestTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<UserDirectoryProjector, UserTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<UserAccessProjector, UserAccessTag>>();
            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<GenericTagMultiProjector<EquipmentTypeProjector, EquipmentTypeTag>>();
        });
    }
}
