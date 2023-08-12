using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

/// <summary>
///     Defines extension methods for Aggregate Query Types.
/// </summary>
public static class AggregateQueryTypesExtensions
{
    /// <summary>
    ///     Checks if the given type is an Aggregate Query Type.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public static bool IsAggregateQueryType(this Type query) => query.DoesImplementingFromGenericInterfaceType(typeof(IAggregateQuery<,,>));

    /// <summary>
    ///     Gets the Aggregate Type from the given Aggregate Query Type.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public static Type GetAggregateTypeFromAggregateQueryType(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IAggregateQuery<,,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Gets the Parameter Type from the given Aggregate Query Type.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public static Type GetParamTypeFromAggregateQueryType(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IAggregateQuery<,,>));
        return baseType.GenericTypeArguments[1];
    }
    /// <summary>
    ///     Get the Response Type from the given Aggregate Query Type.
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public static Type GetResponseTypeFromAggregateQueryType(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IAggregateQuery<,,>));
        return baseType.GenericTypeArguments[2];
    }
}
