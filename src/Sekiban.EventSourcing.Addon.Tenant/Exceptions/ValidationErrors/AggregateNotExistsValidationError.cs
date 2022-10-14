using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;

public class AggregateNotExistsValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public AggregateNotExistsValidationError(string? fieldName) : base(
        string.Format(ExceptionMessages.AggregateNotExistsValidationErrorMessage, fieldName))
    {
    }
}
