using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

public static class SingleProjectionQueryTypesExtensions
{
    public static bool IsSingleProjectionQueryType(this Type singleProjectionQuery) =>
        singleProjectionQuery.DoesImplementingFromGenericInterfaceType(typeof(ISingleProjectionQuery<,,>));

    public static Type GetSingleProjectionTypeFromSingleProjectionQueryType(this Type singleProjectionQuery)
    {
        var baseType =
            singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetParamTypeFromSingleProjectionQueryType(this Type singleProjectionQuery)
    {
        var baseType =
            singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }

    public static Type GetResponseTypeFromSingleProjectionQueryType(this Type singleProjectionQuery)
    {
        var baseType =
            singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
