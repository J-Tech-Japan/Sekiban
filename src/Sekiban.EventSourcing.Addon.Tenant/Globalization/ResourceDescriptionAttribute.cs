namespace Sekiban.EventSourcing.Addon.Tenant.Globalization;

public class ResourceDescriptionAttribute : ResourceAttributeBase
{
    public ResourceDescriptionAttribute(Type resourceClassType, string resourceKeyName)
        : base(resourceClassType, resourceKeyName)
    { }

    public string? Description => ResourceManager?.GetString(ResourceKeyName);
}
