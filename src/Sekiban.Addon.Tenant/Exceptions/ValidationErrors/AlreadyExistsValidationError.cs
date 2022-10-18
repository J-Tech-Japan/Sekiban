using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Properties;
namespace Sekiban.Addon.Tenant.Exceptions.ValidationErrors;

public class AlreadyExistsValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public AlreadyExistsValidationError(string? existsFieldName) : base(
        string.Format(ExceptionMessages.AlreadyExistsValidationError, existsFieldName))
    {
    }
}
