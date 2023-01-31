using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
using System.Reflection;
namespace Sekiban.Core.Types;

public static class AggregateTypesExtensions
{
    public static IEnumerable<TypeInfo> GetAggregateTypes(this IEnumerable<TypeInfo> types)
    {
        return types.Where(
            x => x.IsAggregateType());
    }

    public static bool IsAggregateType(this TypeInfo type) => type.IsClass &&
        type.ImplementedInterfaces.Contains(typeof(IAggregatePayload)) &&
        !type.ImplementedInterfaces.Contains(typeof(ISingleProjectionPayloadCommon));

    public static bool IsParentAggregateType(this Type type) =>
        type.DoesImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>));
    public static Type GetParentAggregatePayloadTypeFromAggregate(this Type aggregateType)
    {
        if (aggregateType.IsParentAggregateType())
        {
            var baseType = aggregateType.GetImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>));
            return baseType.GenericTypeArguments[0];
        }
        throw new Exception(aggregateType.FullName + " is not an aggregate");
    }
    public static Type GetFirstAggregatePayloadTypeFromAggregate(this Type aggregateType)
    {
        if (aggregateType.IsParentAggregateType())
        {
            var baseType = aggregateType.GetImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>));
            return baseType.GenericTypeArguments[1];
        }
        throw new Exception(aggregateType.FullName + " is not an aggregate");
    }
    public static bool IsAggregateType(this Type type) => type.GetTypeInfo().IsAggregateType();

    public static Type GetAggregatePayloadTypeFromAggregate(this Type aggregateType)
    {
        if (aggregateType.IsGenericType && aggregateType.GetGenericTypeDefinition() == typeof(Aggregate<>))
        {
            return aggregateType.GenericTypeArguments[0];
        }
        throw new Exception(aggregateType.FullName + " is not an aggregate");
    }
}
