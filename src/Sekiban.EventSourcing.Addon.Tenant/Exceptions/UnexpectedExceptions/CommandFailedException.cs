using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.UnexpectedExceptions;

public class CommandFailedException : ApplicationException, ISekibanAddonEventSourcingException
{
    public CommandFailedException(Type commandType) : base(string.Format(ExceptionMessages.CommandFailedExceptionMessage, commandType.Name)) { }
}
