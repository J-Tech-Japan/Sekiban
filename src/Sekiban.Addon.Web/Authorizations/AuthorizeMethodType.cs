namespace Sekiban.Addon.Web.Authorizations;

public enum AuthorizeMethodType
{
    CreateCommand,
    ChangeCommand,
    Get,
    List,
    AggregateInfo,
    EventHistory,
    CommandHistory,
    SingleProjection,
    MultiProjection,
    MultiListProjection,
    SendUpdateMarker,
    SnapshotHistory
}
