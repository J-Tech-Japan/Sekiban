using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
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
    public static Type GetAggregatePayloadOrSingleProjectionPayloadTypeFromSingleProjectionListStateType(
        this Type singleProjectionListStateType)
    {
        return (
                singleProjectionListStateType.IsGenericType &&
                singleProjectionListStateType.GetGenericTypeDefinition() == typeof(SingleProjectionListState<>),
                singleProjectionListStateType.GetGenericArguments()[0]) switch
            {
                (true, { IsGenericType: true } t) when t.GetGenericTypeDefinition() == typeof(AggregateState<>) => t
                    .GetGenericArguments()[0],
                (true, { IsGenericType: true } t) when t.GetGenericTypeDefinition() == typeof(SingleProjectionState<>)
                    => t.GetGenericArguments()[0],
                _ => throw new SekibanSingleProjectionPayloadNotExistsException(
                    singleProjectionListStateType.FullName + " is not an Single Projection List state")
            };
    }

    /// <summary>
    ///     Check whether the given type is single projection list state type or not.
    /// </summary>
    /// <param name="singleProjectionListStateType"></param>
    /// <returns></returns>
    public static bool IsSingleProjectionListStateType(this Type singleProjectionListStateType) =>
        singleProjectionListStateType.IsGenericType &&
        singleProjectionListStateType.GetGenericTypeDefinition() == typeof(SingleProjectionListState<>);
}
