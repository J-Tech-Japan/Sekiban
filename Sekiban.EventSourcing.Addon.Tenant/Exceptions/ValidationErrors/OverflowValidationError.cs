using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;

public class OverflowValidationError : OverflowException, ISekibanAddonEventSourcingValidationError
{
    public OverflowValidationError(string? fieldName, int maxLength) : base(
        string.Format(ExceptionMessages.OverflowValidationErrorMessage, fieldName, maxLength)) { }
}
