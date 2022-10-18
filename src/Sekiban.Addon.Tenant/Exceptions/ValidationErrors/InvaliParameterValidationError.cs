using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Properties;
namespace Sekiban.Addon.Tenant.Exceptions.ValidationErrors;

public class InvaliParameterValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public InvaliParameterValidationError() : base(ExceptionMessages.InvaliParameterValidationErrorMessage) { }

    public InvaliParameterValidationError(params string[] messages) : base(string.Join(' ', messages)) { }
}
