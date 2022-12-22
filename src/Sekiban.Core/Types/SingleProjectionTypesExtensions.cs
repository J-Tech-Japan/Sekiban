using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using System.Reflection;
namespace Sekiban.Core.Types;

public static class SingleProjectionTypesExtensions
{
    public static IEnumerable<TypeInfo> GetSingleProjectorTypes(this IEnumerable<TypeInfo> types)
    {
        return types.Where(
            x => x.IsSingleProjectionPayloadType());
    }

    public static bool IsSingleProjectionType(this TypeInfo type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SingleProjection<>);

    public static bool IsSingleProjectionType(this Type type) => type.GetTypeInfo().IsSingleProjectionType();

    public static Type GetSingleProjectionPayloadFromSingleProjectionType(this Type type)
    {
        if (type.IsSingleProjectionType())
        {
            return type.GenericTypeArguments[0];
        }
        throw new Exception(type.FullName + "is not Single Projection Type");
    }

    public static bool IsSingleProjectionPayloadType(this Type typeInfo) => IsSingleProjectionPayloadType(typeInfo.GetTypeInfo());

    public static bool IsSingleProjectionPayloadType(this TypeInfo typeInfo) =>
        typeInfo.DoesImplementingFromGenericInterfaceType(typeof(ISingleProjectionPayload<,>));

    public static Type GetOriginalTypeFromSingleProjectionPayload(this Type singleProjectionType) =>
        GetOriginalTypeFromSingleProjectionPayload(singleProjectionType.GetTypeInfo());

    public static Type GetOriginalTypeFromSingleProjectionPayload(this TypeInfo singleProjectionTypeInfo)
    {
        if (singleProjectionTypeInfo.IsSingleProjectionPayloadType())
        {
            var implementedType = singleProjectionTypeInfo.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionPayload<,>));
            return implementedType.GenericTypeArguments[0];
        }
        throw new SekibanTypeNotFoundException("Can not find original type of " + singleProjectionTypeInfo.Name);
    }

    public static Type GetProjectionTypeFromSingleProjection(this TypeInfo singleProjectionTypeInfo)
    {
        if (singleProjectionTypeInfo.IsSingleProjectionPayloadType())
        {
            var implementedType = singleProjectionTypeInfo.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionPayload<,>));
            return implementedType.GenericTypeArguments[1];
        }
        throw new SekibanTypeNotFoundException("Can not find original type of " + singleProjectionTypeInfo.Name);
    }
}
