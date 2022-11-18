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
        !type.ImplementedInterfaces.Contains(typeof(ISingleProjectionPayload));
}
