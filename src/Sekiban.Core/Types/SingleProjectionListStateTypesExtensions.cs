using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Types;

/// <summary>
///     Single projection list state types extensions.
/// </summary>
public static class SingleProjectionListStateTypesExtensions
{
    /// <summary>
    ///     Get aggregate payload or single projection payload type from the given single projection list state type.
    /// </summary>
    /// <param name="singleProjectionListStateType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Type GetAggregatePayloadOrSingleProjectionPayloadTypeFromSingleProjectionListStateType(this Type singleProjectionListStateType)
    {
        if (singleProjectionListStateType.IsGenericType &&
            singleProjectionListStateType.GetGenericTypeDefinition() == typeof(SingleProjectionListState<>))
        {
            var aggregateStateType = singleProjectionListStateType.GetGenericArguments()[0];
            if (aggregateStateType.IsGenericType && aggregateStateType.GetGenericTypeDefinition() == typeof(AggregateState<>))
            {
                return aggregateStateType.GetGenericArguments()[0];
            }
            return aggregateStateType.IsGenericType && aggregateStateType.GetGenericTypeDefinition() == typeof(SingleProjectionState<>)
                ? aggregateStateType.GetGenericArguments()[0]
                : throw new Exception(singleProjectionListStateType.FullName + " is not an Single Projection List state");
        }
        throw new Exception(singleProjectionListStateType.FullName + " is not an Single Projection List state");
    }
    /// <summary>
    ///     Check whether the given type is single projection list state type or not.
    /// </summary>
    /// <param name="singleProjectionListStateType"></param>
    /// <returns></returns>
    public static bool IsSingleProjectionListStateType(this Type singleProjectionListStateType)
    {
        return singleProjectionListStateType.IsGenericType &&
            singleProjectionListStateType.GetGenericTypeDefinition() == typeof(SingleProjectionListState<>);
    }
}
