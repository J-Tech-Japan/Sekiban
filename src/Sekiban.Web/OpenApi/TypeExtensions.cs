namespace Sekiban.Web.OpenApi;

public static class TypeExtensions
{
    public static bool IsAssignableToGenericType(this Type givenType, Type openGenericType, out Type? closedGenericType)
    {
        if (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == openGenericType)
        {
            closedGenericType = givenType;
            return true;
        }

        if (givenType.BaseType is { } baseType && baseType.IsAssignableToGenericType(openGenericType, out closedGenericType))
        {
            return true;
        }

        foreach (var it in givenType.GetInterfaces())
        {
            if (it.IsAssignableToGenericType(openGenericType, out closedGenericType))
            {
                return true;
            }
        }

        closedGenericType = null;
        return false;
    }
}
