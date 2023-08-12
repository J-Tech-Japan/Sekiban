using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

/// <summary>
///     General list query types extensions.
/// </summary>
public static class GeneralListQueryTypesExtensions
{
    /// <summary>
    ///     Check whether the type is general list query type or not.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public static bool IsGeneralListQueryType(this Type query) => query.DoesImplementingFromGenericInterfaceType(typeof(IGeneralListQuery<,>));

    /// <summary>
    ///     Get the parameter type from the given general list query type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetParamTypeFromGeneralListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IGeneralListQuery<,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get the response type from the given general list query type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetResponseTypeFromGeneralListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IGeneralListQuery<,>));
        return baseType.GenericTypeArguments[1];
    }
}
