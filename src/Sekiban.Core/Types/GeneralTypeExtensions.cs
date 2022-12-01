namespace Sekiban.Core.Types;

public static class GeneralTypeExtensions
{
    public static bool DoesInheritFromGenericType(this Type type, Type genericType)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (genericType == null) throw new ArgumentNullException(nameof(genericType));

        if (!genericType.IsGenericTypeDefinition)
            throw new ArgumentException("The genericType must be a generic type definition.", nameof(genericType));

        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType) return true;

        return type.BaseType != null && DoesInheritFromGenericType(type.BaseType, genericType);
    }

    public static Type GetInheritFromGenericType(this Type type, Type genericType)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (genericType == null) throw new ArgumentNullException(nameof(genericType));

        if (!genericType.IsGenericTypeDefinition)
            throw new ArgumentException("The genericType must be a generic type definition.", nameof(genericType));

        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericType) return type;

        return type.BaseType != null
            ? GetInheritFromGenericType(type.BaseType, genericType)
            : throw new ArgumentException(
                "The type does not implement the generic interface.",
                nameof(type));
    }

    public static bool DoesImplementingFromGenericInterfaceType(this Type type, Type genericInterface)
    {
        return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface);
    }

    public static Type GetImplementingFromGenericInterfaceType(this Type type, Type genericInterface)
    {
        return type.GetInterfaces()
                   .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericInterface) ??
               throw new ArgumentException("The type does not implement the generic interface.", nameof(type));
    }
}
