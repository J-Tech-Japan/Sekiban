using Sekiban.Addon.Tenant.Exceptions.Bases;
namespace Sekiban.Addon.Tenant.Exceptions.UnexpectedExceptions;

public class DataNotFoundException : ApplicationException, ISekibanAddonEventSourcingException
{
}
