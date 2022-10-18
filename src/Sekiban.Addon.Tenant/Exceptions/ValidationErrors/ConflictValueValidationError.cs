using Sekiban.Addon.Tenant.Exceptions.Bases;
using Sekiban.Addon.Tenant.Extensions;
using Sekiban.Addon.Tenant.Properties;
namespace Sekiban.Addon.Tenant.Exceptions.ValidationErrors;

public class ConflictValueValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public ConflictValueValidationError(string? fieldName, object? fieldValue) : base(
        string.Format(ExceptionMessages.ConflictValueValidationErrorMessage, fieldName, fieldValue))
    {
    }

    public static ConflictValueValidationError Create<T>(T command, string propertyName) where T : class
    {
        return new(command.GetType().GetMemberDisplayName(propertyName) ?? propertyName, command.GetPropertyValue(propertyName) ?? string.Empty);
    }
}
