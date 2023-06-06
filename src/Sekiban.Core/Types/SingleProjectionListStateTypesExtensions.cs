using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Types;

public static class SingleProjectionListStateTypesExtensions
{
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
            if (aggregateStateType.IsGenericType && aggregateStateType.GetGenericTypeDefinition() == typeof(SingleProjectionState<>))
            {
                return aggregateStateType.GetGenericArguments()[0];
            }
            throw new Exception(singleProjectionListStateType.FullName + " is not an Single Projection List state");
        }
        throw new Exception(singleProjectionListStateType.FullName + " is not an Single Projection List state");
    }

    public static bool IsSingleProjectionListStateType(this Type singleProjectionListStateType)
    {
        if (singleProjectionListStateType.IsGenericType &&
            singleProjectionListStateType.GetGenericTypeDefinition() == typeof(SingleProjectionListState<>))
        {
            return true;
        }
        return false;
    }
}
