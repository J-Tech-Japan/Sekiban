using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.UnexpectedExceptions;

public class OperationFailedException : ApplicationException, ISekibanAddonEventSourcingException
{
    public OperationFailedException(params string[] messages) : base(string.Join(' ', messages)) { }
}
