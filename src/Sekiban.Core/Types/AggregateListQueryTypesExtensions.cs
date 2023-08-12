using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

/// <summary>
///     Detects whether the type is Aggregate List Query Type or not.
/// </summary>
public static class AggregateListQueryTypesExtensions
{
    /// <summary>
    ///     Detects whether the type is Aggregate List Query Type or not.
    /// </summary>
    /// <param name="aggregateQuery"></param>
    /// <returns></returns>
    public static bool IsAggregateListQueryType(this Type aggregateQuery) =>
        aggregateQuery.DoesImplementingFromGenericInterfaceType(typeof(IAggregateListQuery<,,>));
    /// <summary>
    ///     Get Aggregate Type from Aggregate List Query Type.
    /// </summary>
    /// <param name="aggregateQueryType"></param>
    /// <returns></returns>
    public static Type GetAggregateTypeFromAggregateListQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IAggregateListQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get Parameter Type from Aggregate List Query Type.
    /// </summary>
    /// <param name="aggregateQueryType"></param>
    /// <returns></returns>
    public static Type GetParamTypeFromAggregateListQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IAggregateListQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }
    /// <summary>
    ///     Get Response Type from Aggregate List Query Type.
    /// </summary>
    /// <param name="aggregateQueryType"></param>
    /// <returns></returns>
    public static Type GetResponseTypeFromAggregateListQueryType(this Type aggregateQueryType)
    {
        var baseType = aggregateQueryType.GetImplementingFromGenericInterfaceType(typeof(IAggregateListQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
