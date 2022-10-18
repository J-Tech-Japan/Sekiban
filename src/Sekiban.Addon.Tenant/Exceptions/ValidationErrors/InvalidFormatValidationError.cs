using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Properties;
namespace Sekiban.Addon.Tenant.Exceptions.ValidationErrors;

public class InvalidFormatValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public InvalidFormatValidationError(string? fieldName) : base(string.Format(ExceptionMessages.InvalidFormatValidationErrorMessage, fieldName)) { }
}
