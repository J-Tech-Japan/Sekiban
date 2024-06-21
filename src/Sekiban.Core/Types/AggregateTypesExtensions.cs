using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.SingleProjections;
using System.Reflection;
namespace Sekiban.Core.Types;

/// <summary>
///     Aggregate Types extensions.
/// </summary>
public static class AggregateTypesExtensions
{
    /// <summary>
    ///     Get Aggregate Payload types from given types.
    /// </summary>
    /// <param name="types"></param>
    /// <returns></returns>
    public static IEnumerable<TypeInfo> GetAggregatePayloadTypes(this IEnumerable<TypeInfo> types)
    {
        return types.Where(x => x.IsAggregatePayloadType());
    }

    /// <summary>
    ///     Check if given type is Aggregate Payload Type.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsAggregatePayloadType(this TypeInfo type) =>
        type.ImplementedInterfaces.Contains(typeof(IAggregatePayloadCommon)) &&
        (type.IsClass || type.DoesImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<>))) &&
        !type.IsAggregateSubtypePayload() &&
        !type.ImplementedInterfaces.Contains(typeof(ISingleProjectionPayloadCommon));

    /// <summary>
    ///     Check if given type is Aggregate Payload Type.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsAggregatePayloadType(this Type type) => type.GetTypeInfo().IsAggregatePayloadType();

    /// <summary>
    ///     Check if given type is Aggregate Payload Parent Type.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsParentAggregatePayload(this Type type) => type.DoesImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>));
    /// <summary>
    ///     Check if given type is Aggregate Payload Subtype.
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static bool IsAggregateSubtypePayload(this Type type) =>
        type.DoesImplementingFromGenericInterfaceType(typeof(IAggregateSubtypePayload<,>));
    /// <summary>
    ///     Get base Aggregate Payload Type from given Aggregate Payload.
    /// </summary>
    /// <param name="aggregatePayload"></param>
    /// <returns></returns>
    public static Type GetBaseAggregatePayloadType(this IAggregatePayloadCommon aggregatePayload) =>
        aggregatePayload.GetType().GetBaseAggregatePayloadTypeFromAggregate();
    /// <summary>
    ///     Get base Aggregate Payload Type from given Aggregate Payload Type.
    ///     If given type is parent aggregate payload, it returns the type itself.
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Type GetBaseAggregatePayloadTypeFromAggregate(this Type aggregateType)
    {
        if (aggregateType.IsAggregateSubtypePayload())
        {
            var baseType = aggregateType.GetImplementingFromGenericInterfaceType(typeof(IAggregateSubtypePayload<,>));
            return baseType.GenericTypeArguments[0];
        }
        if (aggregateType.IsParentAggregatePayload())
        {
            var baseType = aggregateType.GetImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>));
            return baseType.GenericTypeArguments[0];
        }
        return aggregateType.GetInterfaces().Any(m => m == typeof(IAggregatePayloadCommon))
            ? aggregateType
            : throw new SekibanAggregatePayloadNotExistsException(aggregateType.FullName + " is not an aggregate");
    }
    /// <summary>
    ///     Get first Aggregate Payload type from given Aggregate Payload.
    ///     First Aggregate Payload means the type that aggregate starts with.
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Type GetFirstAggregatePayloadTypeFromAggregate(this Type aggregateType)
    {
        if (aggregateType.IsParentAggregatePayload())
        {
            var baseType = aggregateType.GetImplementingFromGenericInterfaceType(typeof(IParentAggregatePayload<,>));
            return baseType.GenericTypeArguments[1];
        }
        return aggregateType.GetInterfaces().Any(m => m == typeof(IAggregatePayloadCommon))
            ? aggregateType
            : throw new SekibanAggregatePayloadNotExistsException(aggregateType.FullName + " is not an aggregate");
    }

    /// <summary>
    ///     Check if given type is Aggregate Type.
    /// </summary>
    /// <param name="aggregate"></param>
    /// <returns></returns>
    public static bool IsAggregateType(this IAggregateCommon aggregate) => aggregate.GetType().GetGenericTypeDefinition() == typeof(Aggregate<>);
    /// <summary>
    ///     Check if given type is Aggregate State Type.
    /// </summary>
    /// <param name="aggregate"></param>
    /// <returns></returns>
    public static bool IsAggregateStateType(this IAggregateCommon aggregate) =>
        aggregate.GetType().GetGenericTypeDefinition() == typeof(AggregateState<>);

    /// <summary>
    ///     Get Aggregate Payload Type from given Aggregate.
    /// </summary>
    /// <param name="aggregate"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Type GetAggregatePayloadTypeFromAggregate(this IAggregateCommon aggregate)
    {
        if (aggregate.IsAggregateType() || aggregate.IsAggregateStateType())
        {
            var genericArguments = aggregate.GetType().GetGenericArguments();
            return genericArguments[0];
        }
        throw new SekibanAggregatePayloadNotExistsException("Not an aggregate");
    }
    /// <summary>
    ///     Get Aggregate Payload Type from given Aggregate.
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Type GetAggregatePayloadTypeFromAggregate(this Type aggregateType) =>
        aggregateType.IsGenericType && aggregateType.GetGenericTypeDefinition() == typeof(Aggregate<>)
            ? aggregateType.GenericTypeArguments[0]
            : throw new SekibanAggregatePayloadNotExistsException(aggregateType.FullName + " is not an aggregate");
    /// <summary>
    ///     Get Aggregate Payload Type from given Aggregate State.
    /// </summary>
    /// <param name="aggregateStateType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static Type GetAggregatePayloadTypeFromAggregateState(this Type aggregateStateType) =>
        aggregateStateType.IsGenericType && aggregateStateType.GetGenericTypeDefinition() == typeof(AggregateState<>)
            ? aggregateStateType.GenericTypeArguments[0]
            : throw new SekibanAggregatePayloadNotExistsException(aggregateStateType.FullName + " is not an aggregate state");
}
