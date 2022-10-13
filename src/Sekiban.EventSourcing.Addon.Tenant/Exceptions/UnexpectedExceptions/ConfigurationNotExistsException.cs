using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
using System.Configuration;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.UnexpectedExceptions;

public class ConfigurationNotExistsException : ConfigurationErrorsException, ISekibanAddonEventSourcingException
{
    public ConfigurationNotExistsException(string configurationName) : base(
        string.Format(ExceptionMessages.ConfigurationNotExistsException, configurationName))
    {
    }
}
