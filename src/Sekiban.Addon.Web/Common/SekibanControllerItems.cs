using Sekiban.Core.Dependency;
namespace Sekiban.Addon.Web.Common;

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
    public static SekibanControllerItems FromDependencies(params IDependencyDefinition[] dependencyDefinitions)
    {
        return new SekibanControllerItems(
            dependencyDefinitions.SelectMany(s => s.GetControllerAggregateTypes()).ToArray(),
            dependencyDefinitions.SelectMany(s => s.GetCommandDependencies()).ToArray(),
            dependencyDefinitions.SelectMany(s => s.GetSingleAggregateProjectionTypes()).ToArray(),
            dependencyDefinitions.SelectMany(s => s.GetAggregateListQueryFilterTypes()).ToArray(),
            dependencyDefinitions.SelectMany(s => s.GetAggregateQueryFilterTypes()).ToArray(),
            dependencyDefinitions.SelectMany(s => s.GetSingleAggregateProjectionListQueryFilterTypes()).ToArray(),
            dependencyDefinitions.SelectMany(s => s.GetSingleAggregateProjectionQueryFilterTypes()).ToArray(),
            dependencyDefinitions.SelectMany(s => s.GetProjectionQueryFilterTypes()).ToArray(),
            dependencyDefinitions.SelectMany(s => s.GetProjectionListQueryFilterTypes()).ToArray());
    }
}
