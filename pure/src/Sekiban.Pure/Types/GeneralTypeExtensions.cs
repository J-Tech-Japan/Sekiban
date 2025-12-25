using System.Reflection;
namespace Sekiban.Pure.Types;

/// <summary>
///     Base methods for type checking.
/// </summary>
public static class GeneralTypeExtensions
{
    /// <summary>
    ///     Check given type is inheriting generic type or not.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="genericType"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static bool DoesInheritFromGenericType(this Type type, Type genericType)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(genericType);

        if (!genericType.IsGenericTypeDefinition)
        {
            throw new ArgumentException("The genericType must be a generic type definition.", nameof(genericType));
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() == genericType ||
            type.BaseType != null && DoesInheritFromGenericType(type.BaseType, genericType);
    }

    public static MethodInfo? GetMethodFlex(
        this Type type,
        string name,
        BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
    {
        return type.GetMethod(name, bindingAttr) ?? type.GetMethods().FirstOrDefault(m => m.Name.EndsWith($".{name}"));
    }

    /// <summary>
    ///     Get the type that inherits from the generic type.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="genericType"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static Type GetInheritFromGenericType(this Type type, Type genericType)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(genericType);

        return (type, genericType) switch
        {
            (_, { IsGenericTypeDefinition: false }) => throw new ArgumentException(
                "The genericType must be a generic type definition.",
                nameof(genericType)),
            ({ IsGenericType: true } t, var g) when g == t.GetGenericTypeDefinition() => t,
            ({ BaseType: not null } t, var g) => GetInheritFromGenericType(t.BaseType, g),
            _ => throw new ArgumentException("The type does not implement the generic interface.", nameof(type))
        };
    }

    /// <summary>
    ///     Check given type is implementing generic interface or not.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="genericInterface"></param>
    /// <returns></returns>
    public static bool DoesImplementingFromGenericInterfaceType(this Type type, Type genericInterface)
    {
        return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface);
    }

    /// <summary>
    ///     Get the type that implements the generic interface.
    /// </summary>
    /// <param name="type"></param>
    /// <param name="genericInterface"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static Type GetImplementingFromGenericInterfaceType(this Type type, Type genericInterface)
    {
        return type
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface) ??
            throw new ArgumentException("The type does not implement the generic interface.", nameof(type));
    }

    /// <summary>
    ///     Create Default Instance
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static dynamic CreateDefaultInstance(this Type type)
    {
        if (type == typeof(string))
        {
            return "";
        }
        if (type == typeof(char[]))
        {
            return Array.Empty<char>();
        }
        if (type == typeof(short))
        {
            return 0;
        }
        if (type == typeof(int))
        {
            return 0;
        }
        if (type == typeof(uint))
        {
            return 0;
        }
        if (type == typeof(long))
        {
            return 0;
        }
        if (type == typeof(ulong))
        {
            return 0;
        }
        if (type == typeof(Guid))
        {
            return Guid.Empty;
        }
        var defaultConstructor = type.GetConstructor(Type.EmptyTypes);
        if (defaultConstructor != null)
        {
            return Activator.CreateInstance(type) ?? "No default object found";
        }
        var firstConstructor = type.GetConstructors().FirstOrDefault();

        if (firstConstructor != null)
        {
            var ctorParameters = firstConstructor.GetParameters();
            var parameters = new object[ctorParameters.Length];
            for (var i = 0; i < ctorParameters.Length; i++)
            {
                parameters[i] = CreateDefaultInstance(ctorParameters[i].ParameterType);
            }
            return firstConstructor.Invoke(parameters);
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var genericType = type.GetGenericArguments()[0];
            var constructedListType = typeof(List<>).MakeGenericType(genericType);
            var genericTypeList = (dynamic?)Activator.CreateInstance(constructedListType) ??
                throw new InvalidCastException();
            genericTypeList.Add(genericType.CreateDefaultInstance());
            return genericTypeList;
        }

        return "No default constructor found";
    }
}
