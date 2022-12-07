using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Types;

public static class QueryHandlerTypesExtension
{
    public static bool IsQueryInputType(this Type queryType) => queryType.DoesImplementingFromGenericInterfaceType(typeof(IQueryInput<>));
    public static Type GetOutputClassFromQueryInputType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IQueryInput<>));
        return baseType.GenericTypeArguments[0];
    }
    public static bool IsListQueryInputType(this Type queryType) => queryType.DoesImplementingFromGenericInterfaceType(typeof(IListQueryInput<>));
    public static Type GetOutputClassFromListQueryInputType(this Type queryType)
    {
        var baseType = queryType.GetImplementingFromGenericInterfaceType(typeof(IListQueryInput<>));
        return baseType.GenericTypeArguments[0];
    }
    public static dynamic? GetQueryObjectFromQueryInputType(this IServiceProvider serviceProvider, Type inputType, Type outputType)
    {
        var baseType = typeof(IListHandlerCommon<,>);
        var handlerType = baseType.MakeGenericType(inputType, outputType) ?? throw new Exception("Can not create handler type");
        return serviceProvider.GetService(handlerType);
    }
}
