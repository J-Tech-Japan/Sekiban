using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

public static class GeneralListQueryTypesExtensions
{
    public static bool IsGeneralListQueryType(this Type query) => query.DoesImplementingFromGenericInterfaceType(typeof(IGeneralListQuery<,>));

    public static Type GetParamTypeFromGeneralListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IGeneralListQuery<,>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetResponseTypeFromGeneralListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IGeneralListQuery<,>));
        return baseType.GenericTypeArguments[1];
    }
}
