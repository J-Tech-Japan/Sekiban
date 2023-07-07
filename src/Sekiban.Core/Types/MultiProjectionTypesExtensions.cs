using Sekiban.Core.Query.MultiProjections;
using System.Reflection;
namespace Sekiban.Core.Types;

public static class MultiProjectionTypesExtensions
{

    public static bool IsMultiProjectionType(this Type type) => type.GetTypeInfo().IsMultiProjectionType();

    public static bool IsMultiProjectionType(this TypeInfo type) => type.IsClass && type.DoesInheritFromGenericType(typeof(MultiProjection<>));

    public static bool IsMultiProjectionPayloadType(this Type type) => type.GetTypeInfo().IsMultiProjectionPayloadType();
    public static bool IsMultiProjectionPayloadType(this TypeInfo type) =>
        type.ImplementedInterfaces.Contains(typeof(IMultiProjectionPayloadCommon)) && type.GetConstructor(Type.EmptyTypes) != null;

    public static Type GetMultiProjectionPayloadTypeFromMultiProjection(this Type projectionType)
    {
        if (projectionType.IsMultiProjectionType())
        {
            return projectionType.GenericTypeArguments[0];
        }
        throw new Exception(projectionType.FullName + " is not multi projection type");
    }
}
