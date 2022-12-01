using Sekiban.Core.Query.QueryModel;

namespace Sekiban.Core.Types;

public static class MultiProjectionQueryTypesExtensions
{
    public static bool IsMultiProjectionQueryType(this Type aggregateQuery)
    {
        return aggregateQuery.DoesImplementingFromGenericInterfaceType(typeof(IMultiProjectionQuery<,,>));
    }

    public static Type GetMultiProjectionTypeFromMultiProjectionQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetParamTypeFromMultiProjectionQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }

    public static Type GetResponseTypeFromMultiProjectionQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
