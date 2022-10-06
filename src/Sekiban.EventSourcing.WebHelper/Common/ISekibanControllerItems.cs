namespace Sekiban.EventSourcing.WebHelper.Common;

public interface ISekibanControllerItems
{
    public IReadOnlyCollection<Type> SekibanAggregates { get; }
    public IReadOnlyCollection<(Type serviceType, Type? implementationType)> SekibanCommands { get; }
    public IReadOnlyCollection<Type> SingleAggregateProjections { get; }
    public IReadOnlyCollection<Type> AggregateListQueryFilters { get; }
    public IReadOnlyCollection<Type> AggregateQueryFilters { get; }
    public IReadOnlyCollection<Type> SingleAggregateProjectionListQueryFilters { get; }
    public IReadOnlyCollection<Type> SingleAggregateProjectionQueryFilters { get; }
    public IReadOnlyCollection<Type> ProjectionQueryFilters { get; }
    public IReadOnlyCollection<Type> ProjectionListQueryFilters { get; }
}
