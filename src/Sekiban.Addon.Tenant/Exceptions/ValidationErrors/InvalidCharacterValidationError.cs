using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Properties;
namespace Sekiban.Addon.Tenant.Exceptions.ValidationErrors;

public class InvalidCharacterValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public InvalidCharacterValidationError(string? fieldName, string acceptableCharacterTypes) : base(
        string.Format(ExceptionMessages.InvalidCharacterValidationErrorMessage, fieldName, acceptableCharacterTypes))
    {
    }
}
