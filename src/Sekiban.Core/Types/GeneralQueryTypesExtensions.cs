using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

/// <summary>
///     general query types extensions.
/// </summary>
public static class GeneralQueryTypesExtensions
{
    /// <summary>
    ///     Check whether the type is general query type or not.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public static bool IsGeneralQueryType(this Type query) => query.DoesImplementingFromGenericInterfaceType(typeof(IGeneralQuery<,>));
    /// <summary>
    ///     Get the parameter type from the given general query type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetParamTypeFromGeneralQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IGeneralQuery<,>));
        return baseType.GenericTypeArguments[0];
    }

    /// <summary>
    ///     Get the response type from the given general query type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetResponseTypeFromGeneralQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IGeneralQuery<,>));
        return baseType.GenericTypeArguments[1];
    }
}
