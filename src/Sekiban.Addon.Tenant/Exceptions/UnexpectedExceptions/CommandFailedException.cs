using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Properties;
namespace Sekiban.Addon.Tenant.Exceptions.UnexpectedExceptions;

public class CommandFailedException : ApplicationException, ISekibanAddonEventSourcingException
{
    public CommandFailedException(Type commandType) : base(string.Format(ExceptionMessages.CommandFailedExceptionMessage, commandType.Name)) { }
}
