using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

/// <summary>
///     Query handler types extension.
/// </summary>
public static class QueryHandlerTypesExtension
{
    /// <summary>
    ///     Check if the given type is query input type or not.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static bool IsQueryInputType(this Type queryType) => queryType.DoesImplementingFromGenericInterfaceType(typeof(IQueryInput<>));
    /// <summary>
    ///     Check if the given type is query input type or not.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static bool IsQueryNextType(this Type queryType) => queryType.GetInterfaces().Contains(typeof(INextQueryGeneral));
    /// <summary>
    ///     Get the query output types from given query input type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetOutputClassFromQueryInputType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IQueryInput<>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get the query output types from given query input type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetOutputClassFromNextQueryType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(INextQueryGeneral<>));
        return baseType.GenericTypeArguments[0];
    }

    /// <summary>
    ///     Check whether the given type is list query input type or not.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static bool IsListQueryInputType(this Type queryType) => queryType.DoesImplementingFromGenericInterfaceType(typeof(IListQueryInput<>));
    /// <summary>
    ///     Check whether the given type is list query input type or not.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static bool IsListQueryNextType(this Type queryType) => queryType.DoesImplementingFromGenericInterfaceType(typeof(INextListQueryCommon<>));
    /// <summary>
    ///     Get the query output types from given list query input type.
    /// </summary>
    /// <param name="queryType"></param>
    /// <returns></returns>
    public static Type GetOutputClassFromListQueryInputType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IListQueryInput<>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get the query handler object from given list query input type.
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="inputType"></param>
    /// <param name="outputType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static dynamic? GetQueryObjectFromListQueryInputType(this IServiceProvider serviceProvider, Type inputType, Type outputType)
    {
        var baseType = typeof(IListQueryHandlerCommon<,>);
        var handlerType = baseType.MakeGenericType(inputType, outputType) ?? throw new SekibanTypeNotFoundException("Can not create handler type");
        return serviceProvider.GetService(handlerType);
    }
    /// <summary>
    ///     Get the query handler object from given query input type.
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="inputType"></param>
    /// <param name="outputType"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static dynamic? GetQueryObjectFromQueryInputType(this IServiceProvider serviceProvider, Type inputType, Type outputType)
    {
        var baseType = typeof(IQueryHandlerCommon<,>);
        var handlerType = baseType.MakeGenericType(inputType, outputType) ?? throw new SekibanTypeNotFoundException("Can not create handler type");
        return serviceProvider.GetService(handlerType);
    }
}
