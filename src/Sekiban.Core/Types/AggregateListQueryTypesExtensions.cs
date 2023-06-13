using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

public static class AggregateListQueryTypesExtensions
{
    public static bool IsAggregateListQueryType(this Type aggregateQuery) =>
        aggregateQuery.DoesImplementingFromGenericInterfaceType(typeof(IAggregateListQuery<,,>));
    public static Type GetAggregateTypeFromAggregateListQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IAggregateListQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetParamTypeFromAggregateListQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IAggregateListQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }

    public static Type GetResponseTypeFromAggregateListQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IAggregateListQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
