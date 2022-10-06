namespace Sekiban.EventSourcing.WebHelper.Common;

public record SekibanControllerItems(
    IReadOnlyCollection<Type> SekibanAggregates,
    IReadOnlyCollection<(Type serviceType, Type? implementationType)> SekibanCommands,
    IReadOnlyCollection<Type> SingleAggregateProjections,
    IReadOnlyCollection<Type> AggregateListQueryFilters,
    IReadOnlyCollection<Type> AggregateQueryFilters,
    IReadOnlyCollection<Type> SingleAggregateProjectionListQueryFilters,
    IReadOnlyCollection<Type> SingleAggregateProjectionQueryFilters,
    IReadOnlyCollection<Type> ProjectionQueryFilters,
    IReadOnlyCollection<Type> ProjectionListQueryFilters) : ISekibanControllerItems
{
}
