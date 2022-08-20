namespace Sekiban.EventSourcing.Addon.Tenant.Globalization;

public class ResourceDisplayNameAttribute : ResourceAttributeBase
{
    public ResourceDisplayNameAttribute(Type resourceClassType, string resourceKeyName)
        : base(resourceClassType, resourceKeyName)
    { }

    public string? DisplayName => ResourceManager?.GetString(ResourceKeyName);
}