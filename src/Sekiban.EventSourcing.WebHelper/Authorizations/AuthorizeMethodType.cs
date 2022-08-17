namespace Sekiban.EventSourcing.WebHelper.Authorizations;

public enum AuthorizeMethodType
{
    CreateCommand,
    ChangeCommand,
    Get,
    List,
    AggregateInfo,
    EventHistory,
    CommandHistory,
    SingleAggregateProjection,
    MultipleAggregateProjection,
    MultipleAggregateListProjection,
    SendUpdateMarker
}
