using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

public static class SingleProjectionListQueryTypesExtensions
{
    public static bool IsSingleProjectionListQueryType(this Type singleProjectionQuery) =>
        singleProjectionQuery.DoesImplementingFromGenericInterfaceType(typeof(ISingleProjectionListQuery<,,>));

    public static Type GetSingleProjectionTypeFromSingleProjectionListQueryType(this Type singleProjectionQuery)
    {
        var baseType = singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetParamTypeFromSingleProjectionListQueryType(this Type singleProjectionQuery)
    {
        var baseType = singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }

    public static Type GetResponseTypeFromSingleProjectionListQueryType(this Type singleProjectionQuery)
    {
        var baseType = singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
