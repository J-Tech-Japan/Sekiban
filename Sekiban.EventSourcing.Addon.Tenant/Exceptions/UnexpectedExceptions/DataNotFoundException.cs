using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.UnexpectedExceptions;

public class DataNotFoundException : ApplicationException, ISekibanAddonEventSourcingException { }
