using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;

public class RequiredFieldValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public RequiredFieldValidationError(string? fieldName) : base(string.Format(ExceptionMessages.RequiredFieldValidationErrorMessage, fieldName)) { }
}
