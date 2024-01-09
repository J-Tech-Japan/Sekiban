using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using System.Reflection;
namespace Sekiban.Core.Types;

/// <summary>
///     Single Projection Types Extensions.
/// </summary>
public static class SingleProjectionTypesExtensions
{
    /// <summary>
    ///     Get single projection types from given type enumerable.
    /// </summary>
    /// <param name="types"></param>
    /// <returns></returns>
    public static IEnumerable<TypeInfo> GetSingleProjectorTypes(this IEnumerable<TypeInfo> types)
    {
        return types.Where(x => x.IsSingleProjectionPayloadType());
    }
    /// <summary>
    ///     Check whether the given type is Single Projection Type or not.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsSingleProjectionType(this TypeInfo type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SingleProjection<>);
    /// <summary>
    ///     Check whether the given type is Single Projection Type or not.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsSingleProjectionType(this Type type) => type.GetTypeInfo().IsSingleProjectionType();
    /// <summary>
    ///     Get single projection payload type from single projection type.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Type GetSingleProjectionPayloadFromSingleProjectionType(this Type type) =>
        type.IsSingleProjectionType()
            ? type.GenericTypeArguments[0]
            : throw new SekibanSingleProjectionPayloadNotExistsException(type.FullName + "is not Single Projection Type");
    /// <summary>
    ///     Check whether the given type is Single Projection Payload Type or not.
    /// </summary>
    /// <param name="typeInfo"></param>
    /// <returns></returns>
    public static bool IsSingleProjectionPayloadType(this Type typeInfo) => IsSingleProjectionPayloadType(typeInfo.GetTypeInfo());
    /// <summary>
    ///     Check whether the given type is Single Projection Payload Type or not.
    /// </summary>
    /// <param name="typeInfo"></param>
    /// <returns></returns>
    public static bool IsSingleProjectionPayloadType(this TypeInfo typeInfo) =>
        typeInfo.DoesImplementingFromGenericInterfaceType(typeof(ISingleProjectionPayload<,>));
    /// <summary>
    ///     Get the aggregate payload type from the given single projection payload type.
    /// </summary>
    /// <param name="singleProjectionType"></param>
    /// <returns></returns>
    public static Type GetAggregatePayloadTypeFromSingleProjectionPayload(this Type singleProjectionType) =>
        GetAggregatePayloadTypeFromSingleProjectionPayload(singleProjectionType.GetTypeInfo());
    /// <summary>
    ///     Get the aggregate payload type from the given single projection payload type.
    /// </summary>
    /// <param name="singleProjectionTypeInfo"></param>
    /// <returns></returns>
    /// <exception cref="SekibanTypeNotFoundException"></exception>
    public static Type GetAggregatePayloadTypeFromSingleProjectionPayload(this TypeInfo singleProjectionTypeInfo)
    {
        if (singleProjectionTypeInfo.IsSingleProjectionPayloadType())
        {
            var implementedType = singleProjectionTypeInfo.GetImplementingFromGenericInterfaceType(typeof(ISingleProjectionPayload<,>));
            return implementedType.GenericTypeArguments[0];
        }
        throw new SekibanTypeNotFoundException("Can not find original type of " + singleProjectionTypeInfo.Name);
    }
}
