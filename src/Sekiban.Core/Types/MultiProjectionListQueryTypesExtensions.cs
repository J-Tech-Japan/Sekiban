using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

/// <summary>
///     Multi Projection List Query Types Extensions.
/// </summary>
public static class MultiProjectionListQueryTypesExtensions
{
    /// <summary>
    ///     Check whether the type is Multi Projection List Query Type or not.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public static bool IsMultiProjectionListQueryType(this Type query) =>
        query.DoesImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>));
    /// <summary>
    ///     Get the Multi Projection Type from the given Multi Projection List Query Type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetMultiProjectionTypeFromMultiProjectionListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get the parameter type from the given Multi Projection List Query Type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetParamTypeFromMultiProjectionListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }
    /// <summary>
    ///     Get the response type from the given Multi Projection List Query Type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetResponseTypeFromMultiProjectionListQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionListQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
