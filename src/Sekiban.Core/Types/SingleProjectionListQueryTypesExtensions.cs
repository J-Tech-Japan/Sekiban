using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

/// <summary>
///     Single projection list query types extensions.
/// </summary>
public static class SingleProjectionListQueryTypesExtensions
{
    /// <summary>
    ///     Check whether the given type is single projection list query type or not.
    /// </summary>
    /// <param name="singleProjectionQuery"></param>
    /// <returns></returns>
    public static bool IsSingleProjectionListQueryType(this Type singleProjectionQuery) =>
        singleProjectionQuery.DoesImplementingFromGenericInterfaceType(typeof(ISingleProjectionListQuery<,,>));

    public static bool IsSingleProjectionNextQueryType(this Type singleProjectionQuery) =>
        singleProjectionQuery.DoesImplementingFromGenericInterfaceType(typeof(INextSingleProjectionQueryCommon<,>));

    /// <summary>
    ///     Get Single projection type from the given single projection list query type.
    /// </summary>
    /// <param name="singleProjectionQuery"></param>
    /// <returns></returns>
    public static Type GetSingleProjectionTypeFromSingleProjectionListQueryType(this Type singleProjectionQuery)
    {
        var baseType
            = singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get the parameter type from the given single projection list query type.
    /// </summary>
    /// <param name="singleProjectionQuery"></param>
    /// <returns></returns>
    public static Type GetParamTypeFromSingleProjectionListQueryType(this Type singleProjectionQuery)
    {
        var baseType
            = singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }
    /// <summary>
    ///     Get the response type from the given single projection list query type.
    /// </summary>
    /// <param name="singleProjectionQuery"></param>
    /// <returns></returns>
    public static Type GetResponseTypeFromSingleProjectionListQueryType(this Type singleProjectionQuery)
    {
        var baseType
            = singleProjectionQuery.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
