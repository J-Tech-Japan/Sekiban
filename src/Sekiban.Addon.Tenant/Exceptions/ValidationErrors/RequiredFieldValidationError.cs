using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Properties;
namespace Sekiban.Addon.Tenant.Exceptions.ValidationErrors;

public class RequiredFieldValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public RequiredFieldValidationError(string? fieldName) : base(string.Format(ExceptionMessages.RequiredFieldValidationErrorMessage, fieldName)) { }
}
