using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Properties;
namespace Sekiban.Addon.Tenant.Exceptions.ValidationErrors;

public class AggregateNotExistsValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public AggregateNotExistsValidationError(string? fieldName) : base(
        string.Format(ExceptionMessages.AggregateNotExistsValidationErrorMessage, fieldName))
    {
    }
}
