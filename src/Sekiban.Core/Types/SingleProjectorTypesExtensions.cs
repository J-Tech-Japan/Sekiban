using Sekiban.Core.Query.MultiProjections;
using System.Reflection;
namespace Sekiban.Core.Types;

/// <summary>
///     Single Projector Types Extensions
/// </summary>
public static class SingleProjectorTypesExtensions
{
    /// <summary>
    ///     Check whether the type is Single Projector Type or not.
    /// </summary>
    /// <param name="typeInfo"></param>
    /// <returns></returns>
    public static bool IsSingleProjectorType(this TypeInfo typeInfo) =>
        typeInfo.DoesInheritFromGenericType(typeof(SingleProjectionListProjector<,,>));
    /// <summary>
    ///     Check whether the type is Single Projector Type or not.
    /// </summary>
    /// <param name="typeInfo"></param>
    /// <returns></returns>
    public static bool IsSingleProjectorType(this Type typeInfo) => typeInfo.DoesInheritFromGenericType(typeof(SingleProjectionListProjector<,,>));
    /// <summary>
    ///     Get the original aggregate type from the given Single Projector Type.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Type GetOriginalAggregatePayloadTypeFromSingleProjectionListProjector(this Type type)
    {
        var baseType = type.GetInheritFromGenericType(typeof(SingleProjectionListProjector<,,>));
        var projector = baseType.GenericTypeArguments[2];
        var instance = Activator.CreateInstance(projector) as dynamic;
        return instance?.GetOriginalAggregatePayloadType() ?? throw new Exception("Could not get original aggregate type");
    }
}
