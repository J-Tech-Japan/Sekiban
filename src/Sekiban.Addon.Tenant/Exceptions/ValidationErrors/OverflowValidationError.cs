using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Properties;
namespace Sekiban.Addon.Tenant.Exceptions.ValidationErrors;

public class OverflowValidationError : OverflowException, ISekibanAddonEventSourcingValidationError
{
    public OverflowValidationError(string? fieldName, int maxLength) : base(
        string.Format(ExceptionMessages.OverflowValidationErrorMessage, fieldName, maxLength))
    {
    }
}
