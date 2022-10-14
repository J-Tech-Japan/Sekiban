namespace Sekiban.EventSourcing.Addon.Tenant.Globalization;

public class ResourceDescriptionAttribute : ResourceAttributeBase
{

    public string? Description => ResourceManager?.GetString(ResourceKeyName);
    public ResourceDescriptionAttribute(Type resourceClassType, string resourceKeyName) : base(resourceClassType, resourceKeyName)
    {
    }
}
