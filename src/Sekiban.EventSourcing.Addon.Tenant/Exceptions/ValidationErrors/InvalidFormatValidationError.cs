using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;

public class InvalidFormatValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public InvalidFormatValidationError(string? fieldName) : base(string.Format(ExceptionMessages.InvalidFormatValidationErrorMessage, fieldName)) { }
}
