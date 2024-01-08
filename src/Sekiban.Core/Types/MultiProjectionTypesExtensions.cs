using Sekiban.Core.Query.MultiProjections;
using System.Reflection;
namespace Sekiban.Core.Types;

/// <summary>
///     Multi Projection Types Extensions
/// </summary>
public static class MultiProjectionTypesExtensions
{
    /// <summary>
    ///     Check whether the type is Multi Projection Type or not.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsMultiProjectionType(this Type type) => type.GetTypeInfo().IsMultiProjectionType();
    /// <summary>
    ///     Check whether the type is Multi Projection Type or not.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsMultiProjectionType(this TypeInfo type) => type.IsClass && type.DoesInheritFromGenericType(typeof(MultiProjection<>));
    /// <summary>
    ///     Check whether the given type is Multi Projection Payload Type or not.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsMultiProjectionPayloadType(this Type type) => type.GetTypeInfo().IsMultiProjectionPayloadType();
    /// <summary>
    ///     Check whether the given type is Multi Projection Payload Type or not.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsMultiProjectionPayloadType(this TypeInfo type)
    {
        if (!type.DoesImplementingFromGenericInterfaceType(typeof(IMultiProjectionPayloadGeneratePayload<>))) { return false; }
        var genericType = type.GetImplementingFromGenericInterfaceType(typeof(IMultiProjectionPayloadGeneratePayload<>));
        // CreateInitialPayload type should be equal to the type of the payload.
        return genericType.GenericTypeArguments[0] == type;
    }
    /// <summary>
    ///     Get the Multi Projection Payload Type from the given Multi Projection Type.
    /// </summary>
    /// <param name="projectionType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Type GetMultiProjectionPayloadTypeFromMultiProjection(this Type projectionType)
    {
        return projectionType.IsMultiProjectionType()
            ? projectionType.GenericTypeArguments[0]
            : throw new Exception(projectionType.FullName + " is not multi projection type");
    }
}
