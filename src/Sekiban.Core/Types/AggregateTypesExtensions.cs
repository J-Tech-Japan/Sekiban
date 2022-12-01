using System.Reflection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;

namespace Sekiban.Core.Types;

public static class AggregateTypesExtensions
{
    public static IEnumerable<TypeInfo> GetAggregateTypes(this IEnumerable<TypeInfo> types)
    {
        return types.Where(
            x => x.IsAggregateType());
    }

    public static bool IsAggregateType(this TypeInfo type)
    {
        return type.IsClass &&
               type.ImplementedInterfaces.Contains(typeof(IAggregatePayload)) &&
               !type.ImplementedInterfaces.Contains(typeof(ISingleProjectionPayload));
    }

    public static Type GetAggregatePayloadTypeFromAggregate(this Type aggregateType)
    {
        if (aggregateType.IsGenericType && aggregateType.GetGenericTypeDefinition() == typeof(Aggregate<>))
            return aggregateType.GenericTypeArguments[0];
        throw new Exception(aggregateType.FullName + " is not an aggregate");
    }
}
