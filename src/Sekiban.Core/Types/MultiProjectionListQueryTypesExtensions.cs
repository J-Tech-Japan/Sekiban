using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

public static class MultiProjectionListQueryTypesExtensions
{
    public static bool IsMultiProjectionListQueryType(this Type query) =>
        query.DoesImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>));
    public static Type GetMultiProjectionTypeFromMultiProjectionListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }
    public static Type GetParamTypeFromMultiProjectionListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }
    public static Type GetResponseTypeFromMultiProjectionListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
