using Sekiban.Addon.Tenant.Exceptions.Bases;
namespace Sekiban.Addon.Tenant.Exceptions.UnexpectedExceptions;

public class OperationFailedException : ApplicationException, ISekibanAddonEventSourcingException
{
    public OperationFailedException(params string[] messages) : base(string.Join(' ', messages)) { }
}
