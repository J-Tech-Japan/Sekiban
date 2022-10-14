using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;

public class InvalidCharacterValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public InvalidCharacterValidationError(string? fieldName, string acceptableCharacterTypes) : base(
        string.Format(ExceptionMessages.InvalidCharacterValidationErrorMessage, fieldName, acceptableCharacterTypes))
    {
    }
}
