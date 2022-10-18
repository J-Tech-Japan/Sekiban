using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Properties;
using System.Configuration;
namespace Sekiban.Addon.Tenant.Exceptions.UnexpectedExceptions;

public class ConfigurationNotExistsException : ConfigurationErrorsException, ISekibanAddonEventSourcingException
{
    public ConfigurationNotExistsException(string configurationName) : base(
        string.Format(ExceptionMessages.ConfigurationNotExistsException, configurationName))
    {
    }
}
