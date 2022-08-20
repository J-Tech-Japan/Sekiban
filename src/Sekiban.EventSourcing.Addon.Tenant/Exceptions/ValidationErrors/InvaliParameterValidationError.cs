using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;

public class InvaliParameterValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public InvaliParameterValidationError() : base(ExceptionMessages.InvaliParameterValidationErrorMessage) { }

    public InvaliParameterValidationError(params string[] messages) : base(string.Join(' ', messages)) { }
}
