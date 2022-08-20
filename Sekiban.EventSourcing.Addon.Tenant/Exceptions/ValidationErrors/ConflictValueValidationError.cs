using Sekiban.EventSourcing.Addon.Tenant.Exceptions.Bases;
using Sekiban.EventSourcing.Addon.Tenant.Extensions;
using Sekiban.EventSourcing.Addon.Tenant.Properties;
namespace Sekiban.EventSourcing.Addon.Tenant.Exceptions.ValidationErrors;

public class ConflictValueValidationError : ApplicationException, ISekibanAddonEventSourcingValidationError
{
    public ConflictValueValidationError(string? fieldName, object? fieldValue) : base(
        string.Format(ExceptionMessages.ConflictValueValidationErrorMessage, fieldName, fieldValue)) { }

    public static ConflictValueValidationError Create<T>(T command, string propertyName) where T : class =>
        new ConflictValueValidationError(
            command.GetType().GetMemberDisplayName(propertyName) ?? propertyName,
            command.GetPropertyValue(propertyName) ?? string.Empty);
}
