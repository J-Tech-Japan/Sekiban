using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

/// <summary>
///     Multi Projection Query Types Extensions.
/// </summary>
public static class MultiProjectionQueryTypesExtensions
{
    /// <summary>
    ///     Check whether the type is Multi Projection Query Type or not.
    /// </summary>
    /// <param name="aggregateQuery"></param>
    /// <returns></returns>
    public static bool IsMultiProjectionQueryType(this Type aggregateQuery) =>
        aggregateQuery.DoesImplementingFromGenericInterfaceType(typeof(IMultiProjectionQuery<,,>));
    /// <summary>
    ///     Get the Multi Projection Type from the given Multi Projection Query Type.
    /// </summary>
    /// <param name="aggregateQueryType"></param>
    /// <returns></returns>
    public static Type GetMultiProjectionTypeFromMultiProjectionQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get the parameter type from the given Multi Projection Query Type.
    /// </summary>
    /// <param name="aggregateQueryType"></param>
    /// <returns></returns>
    public static Type GetParamTypeFromMultiProjectionQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }
    /// <summary>
    ///     Get the response type from the given Multi Projection Query Type.
    /// </summary>
    /// <param name="aggregateQueryType"></param>
    /// <returns></returns>
    public static Type GetResponseTypeFromMultiProjectionQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
