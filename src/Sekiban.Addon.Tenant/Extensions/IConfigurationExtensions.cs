using Microsoft.Extensions.Configuration;
using Sekiban.Addon.Tenant.Exceptions.UnexpectedExceptions;
namespace Sekiban.Addon.Tenant.Extensions;

public static class IConfigurationExtensions
{
    public static TSettings GetSettings<TSettings>(this IConfiguration configuration) where TSettings : class
    {
        var settings = configuration.GetSection(typeof(TSettings).Name).Get<TSettings>();
        if (settings is null)
        {
            throw new ConfigurationNotExistsException(typeof(TSettings).Name);
        }

        return settings;
    }
}
