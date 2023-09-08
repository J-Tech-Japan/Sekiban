namespace Sekiban.Web.Authorizations;

/// <summary>
///     Authorize method type
/// </summary>
public enum AuthorizeMethodType
{
    Command,
    Get,
    List,
    AggregateInfo,
    EventHistory,
    CommandHistory,
    SingleProjection,
    MultiProjection,
    SendUpdateMarker,
    SnapshotHistory
}
