using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
using System.Reflection;
namespace Sekiban.Core.Types;

public static class AggregateTypesExtensions
{
    public static IEnumerable<TypeInfo> GetAggregateTypes(this IEnumerable<TypeInfo> types)
    {
        return types.Where(
            x => x.IsAggregatePayloadType());
    }

    public static bool IsAggregatePayloadType(this TypeInfo type) => type.ImplementedInterfaces.Contains(typeof(IAggregatePayloadCommon)) &&
        !type.ImplementedInterfaces.Contains(typeof(ISingleProjectionPayloadCommon));

    public static bool IsParentAggregatePayload(this Type type) =>
        type.DoesImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>));
    public static bool IsAggregateSubtypePayload(this Type type) =>
        type.DoesImplementingFromGenericInterfaceType(typeof(IAggregateSubtypePayload<>));
    public static Type GetBaseAggregatePayloadType(this IAggregatePayloadCommon aggregatePayload) =>
        aggregatePayload.GetType().GetBaseAggregatePayloadTypeFromAggregate();
    public static Type GetBaseAggregatePayloadTypeFromAggregate(this Type aggregateType)
    {
        if (aggregateType.IsAggregateSubtypePayload())
        {
            var baseType = aggregateType.GetImplementingFromGenericInterfaceType(typeof(IAggregateSubtypePayload<>));
            return baseType.GenericTypeArguments[0];
        }
        if (aggregateType.IsParentAggregatePayload())
        {
            var baseType = aggregateType.GetImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>));
            return baseType.GenericTypeArguments[0];
        }
        if (aggregateType.GetInterfaces().Any(m => m == typeof(IAggregatePayloadCommon)))
        {
            return aggregateType;
        }
        throw new Exception(aggregateType.FullName + " is not an aggregate");
    }
    public static Type GetFirstAggregatePayloadTypeFromAggregate(this Type aggregateType)
    {
        if (aggregateType.IsParentAggregatePayload())
        {
            var baseType = aggregateType.GetImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>));
            return baseType.GenericTypeArguments[1];
        }
        if (aggregateType.GetInterfaces().Any(m => m == typeof(IAggregatePayloadCommon)))
        {
            return aggregateType;
        }
        throw new Exception(aggregateType.FullName + " is not an aggregate");
    }
    public static bool IsAggregatePayloadType(this Type type) => type.GetTypeInfo().IsAggregatePayloadType();

    public static bool IsAggregateType(this IAggregateCommon aggregate) => aggregate.GetType().GetGenericTypeDefinition() == typeof(Aggregate<>);
    public static Type GetAggregatePayloadTypeFromAggregate(this IAggregateCommon aggregate)
    {
        if (aggregate.IsAggregateType() || aggregate.IsAggregateStateType())
        {
            var genericArguments = aggregate.GetType().GetGenericArguments();
            return genericArguments[0];
        }
        throw new Exception("Not an aggregate");
    }
    public static bool IsAggregateStateType(this IAggregateCommon aggregate) =>
        aggregate.GetType().GetGenericTypeDefinition() == typeof(AggregateState<>);

    public static Type GetAggregatePayloadTypeFromAggregate(this Type aggregateType)
    {
        if (aggregateType.IsGenericType && aggregateType.GetGenericTypeDefinition() == typeof(Aggregate<>))
        {
            return aggregateType.GenericTypeArguments[0];
        }
        throw new Exception(aggregateType.FullName + " is not an aggregate");
    }
}
