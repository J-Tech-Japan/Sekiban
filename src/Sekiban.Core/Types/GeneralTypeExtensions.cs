namespace Sekiban.Core.Types;

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
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }
        if (genericType == null)
        {
            throw new ArgumentNullException(nameof(genericType));
        }

        if (!genericType.IsGenericTypeDefinition)
        {
            throw new ArgumentException("The genericType must be a generic type definition.", nameof(genericType));
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType)
        {
            return true;
        }

        return type.BaseType != null && DoesInheritFromGenericType(type.BaseType, genericType);
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
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }
        if (genericType == null)
        {
            throw new ArgumentNullException(nameof(genericType));
        }

        if (!genericType.IsGenericTypeDefinition)
        {
            throw new ArgumentException("The genericType must be a generic type definition.", nameof(genericType));
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType)
        {
            return type;
        }

        return type.BaseType != null
            ? GetInheritFromGenericType(type.BaseType, genericType)
            : throw new ArgumentException("The type does not implement the generic interface.", nameof(type));
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
        return type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface) ??
            throw new ArgumentException("The type does not implement the generic interface.", nameof(type));
    }
}
