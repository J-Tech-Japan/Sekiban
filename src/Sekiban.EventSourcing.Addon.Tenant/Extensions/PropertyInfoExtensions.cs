namespace Sekiban.EventSourcing.Addon.Tenant.Extensions;

public static class PropertyInfoExtensions
{
    public static object? GetPropertyValue(this object o, string propertyName)
    {
        var pi = o.GetType().GetProperty(propertyName);
        return pi?.GetValue(o);
    }
}
