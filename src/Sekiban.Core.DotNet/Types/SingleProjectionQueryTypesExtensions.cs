using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

/// <summary>
///     Single projection query types extensions.
/// </summary>
public static class SingleProjectionQueryTypesExtensions
{
    /// <summary>
    ///     check whether the given type is single projection query type or not.
    /// </summary>
    /// <param name="singleProjectionQuery"></param>
    /// <returns></returns>
    public static bool IsSingleProjectionQueryType(this Type singleProjectionQuery) =>
        singleProjectionQuery.DoesImplementingFromGenericInterfaceType(typeof(ISingleProjectionQuery<,,>));
    /// <summary>
    ///     Get Single projection type from the given single projection query type.
    /// </summary>
    /// <param name="singleProjectionQuery"></param>
    /// <returns></returns>
    public static Type GetSingleProjectionTypeFromSingleProjectionQueryType(this Type singleProjectionQuery)
    {
        var baseType
            = singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get the parameter type from the given single projection query type.
    /// </summary>
    /// <param name="singleProjectionQuery"></param>
    /// <returns></returns>
    public static Type GetParamTypeFromSingleProjectionQueryType(this Type singleProjectionQuery)
    {
        var baseType
            = singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }
    /// <summary>
    ///     Get the response type from the given single projection query type.
    /// </summary>
    /// <param name="singleProjectionQuery"></param>
    /// <returns></returns>
    public static Type GetResponseTypeFromSingleProjectionQueryType(this Type singleProjectionQuery)
    {
        var baseType
            = singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
