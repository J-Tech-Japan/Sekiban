using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

public static class GeneralQueryTypesExtensions
{
    public static bool IsGeneralQueryType(this Type query) => query.DoesImplementingFromGenericInterfaceType(typeof(IGeneralQuery<,>));

    public static Type GetParamTypeFromGeneralQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IGeneralQuery<,>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetResponseTypeFromGeneralQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IGeneralQuery<,>));
        return baseType.GenericTypeArguments[1];
    }
}
