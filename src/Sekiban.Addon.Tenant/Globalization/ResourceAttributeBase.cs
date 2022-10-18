using System.Resources;
namespace Sekiban.Addon.Tenant.Globalization;

public abstract class ResourceAttributeBase : Attribute
{
    protected ResourceManager? ResourceManager { get; }
    protected string ResourceKeyName { get; }

    public ResourceAttributeBase(Type resourceClassType, string resourceKeyName)
    {
        var resourceNames = resourceClassType.Assembly.GetManifestResourceNames();
        var resourceName = resourceNames.SingleOrDefault(s => s.Contains(resourceClassType.Name));

        ResourceManager = resourceName is null
            ? null
            : new ResourceManager(resourceName.Replace(".resources", string.Empty), resourceClassType.Assembly);
        ResourceKeyName = resourceKeyName;
    }
}
