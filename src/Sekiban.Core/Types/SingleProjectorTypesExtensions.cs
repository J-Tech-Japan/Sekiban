using Sekiban.Core.Query.MultiProjections;
using System.Reflection;
namespace Sekiban.Core.Types;

public static class SingleProjectorTypesExtensions
{
    public static bool IsSingleProjectorType(this TypeInfo typeInfo) =>
        typeInfo.DoesInheritFromGenericType(typeof(SingleProjectionListProjector<,,>));
    public static bool IsSingleProjectorType(this Type typeInfo) => typeInfo.DoesInheritFromGenericType(typeof(SingleProjectionListProjector<,,>));

    public static Type GetOriginalAggregateTypeFromSingleProjectionListProjector(this Type type)
    {
        var baseType = type.GetInheritFromGenericType(typeof(SingleProjectionListProjector<,,>));
        var projector = baseType.GenericTypeArguments[2];
        var instance = Activator.CreateInstance(projector) as dynamic;
        return instance?.OriginalAggregateType() ?? throw new Exception("Could not get original aggregate type");
    }
}
