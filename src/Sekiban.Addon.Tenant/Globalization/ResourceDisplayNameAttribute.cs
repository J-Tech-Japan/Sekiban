namespace Sekiban.Addon.Tenant.Globalization;

public class ResourceDisplayNameAttribute : ResourceAttributeBase
{

    public string? DisplayName => ResourceManager?.GetString(ResourceKeyName);
    public ResourceDisplayNameAttribute(Type resourceClassType, string resourceKeyName) : base(resourceClassType, resourceKeyName)
    {
    }
}
