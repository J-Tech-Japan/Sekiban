using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

public static class AggregateQueryTypesExtensions
{
    public static bool IsAggregateQueryType(this Type query) => query.DoesImplementingFromGenericInterfaceType(typeof(IAggregateQuery<,,>));

    public static Type GetAggregateTypeFromAggregateQueryType(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IAggregateQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetParamTypeFromAggregateQueryType(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IAggregateQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }

    public static Type GetResponseTypeFromAggregateQueryType(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IAggregateQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
