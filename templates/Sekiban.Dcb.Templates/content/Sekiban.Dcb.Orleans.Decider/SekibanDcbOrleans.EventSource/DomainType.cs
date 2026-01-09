using Dcb.ImmutableModels;
using Dcb.MeetingRoomModels;
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

        });
    }
}
