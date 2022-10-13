using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;

public class AlreadyExistsValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public AlreadyExistsValidationError(string? existsFieldName) : base(
        string.Format(ExceptionMessages.AlreadyExistsValidationError, existsFieldName))
    {
    }
}
