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
    public static bool IsSingleProjectionPayloadType(this TypeInfo typeInfo)
    {
        var projectorBase = typeof(SingleProjectionPayloadBase<,>);
        var projectorBaseDeletable = typeof(DeletableSingleProjectionPayloadBase<,>);
        return typeInfo.IsClass &&
            new[] { projectorBase.Name, projectorBaseDeletable.Name }.Contains(typeInfo.BaseType?.Name) &&
            !typeInfo.IsGenericType &&
            typeInfo.BaseType?.Namespace == projectorBase.Namespace;
    }
    public static Type GetOriginalTypeFromSingleProjectionPayload(this Type singleProjectionType) =>
        GetOriginalTypeFromSingleProjectionPayload(singleProjectionType.GetTypeInfo());

    public static Type GetOriginalTypeFromSingleProjectionPayload(this TypeInfo singleProjectionTypeInfo)
    {
        var baseType = singleProjectionTypeInfo.BaseType;
        if (baseType is null) { throw new SekibanTypeNotFoundException("Can not find baseType of " + singleProjectionTypeInfo.Name); }
        if (baseType.IsGenericType &&
            new[] { typeof(SingleProjectionPayloadBase<,>), typeof(DeletableSingleProjectionPayloadBase<,>) }.Contains(
                baseType.GetGenericTypeDefinition()))
        {
            return baseType.GenericTypeArguments[0];
        }
        throw new SekibanTypeNotFoundException("Can not find original type of " + singleProjectionTypeInfo.Name);
    }
    public static Type GetProjectionTypeFromSingleProjection(this TypeInfo singleProjectionTypeInfo)
    {
        var baseType = singleProjectionTypeInfo.BaseType;
        if (baseType is null) { throw new SekibanTypeNotFoundException("Can not find baseType of " + singleProjectionTypeInfo.Name); }
        if (!baseType.IsGenericType ||
            !new[] { typeof(SingleProjectionPayloadBase<,>), typeof(DeletableSingleProjectionPayloadBase<,>) }.Contains(
                baseType.GetGenericTypeDefinition()))
        {
            throw new SekibanTypeNotFoundException("Can not find Projection type of " + singleProjectionTypeInfo.Name);
        }
        var singleProjectionBase = typeof(SingleProjection<>);
        return singleProjectionBase.MakeGenericType(singleProjectionTypeInfo);
    }
}
