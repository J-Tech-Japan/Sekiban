using Sekiban.Dcb.Commands;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace Sekiban.Dcb.Validation;

/// <summary>
///     Provides validation functionality for commands using DataAnnotations attributes
/// </summary>
public static class CommandValidator
{
    /// <summary>
    ///     Validates a command object using DataAnnotations attributes
    /// </summary>
    /// <param name="command">The command to validate</param>
    /// <returns>A list of validation errors, empty if valid</returns>
    public static List<CommandValidationError> ValidateCommand(ICommand command)
    {
        if (command == null)
        {
            return new List<CommandValidationError>
            {
                new("Command", "Command cannot be null")
            };
        }

        var errors = new List<CommandValidationError>();
        var type = command.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var value = property.GetValue(command);
            var validationAttributes = property.GetCustomAttributes<ValidationAttribute>(true);

            foreach (var attribute in validationAttributes)
            {
                var validationContext = new ValidationContext(command)
                {
                    MemberName = property.Name,
                    DisplayName = property.Name
                };

                var result = attribute.GetValidationResult(value, validationContext);
                if (result != ValidationResult.Success && result != null)
                {
                    errors.Add(
                        new CommandValidationError(
                            property.Name,
                            result.ErrorMessage ?? $"Validation failed for {property.Name}",
                            value));
                }
            }

            // Also validate nested objects if they are commands
            if (value is ICommand nestedCommand)
            {
                var nestedErrors = ValidateCommand(nestedCommand);
                foreach (var nestedError in nestedErrors)
                {
                    errors.Add(
                        new CommandValidationError(
                            $"{property.Name}.{nestedError.PropertyName}",
                            nestedError.ErrorMessage,
                            nestedError.AttemptedValue));
                }
            }
        }

        return errors;
    }

    /// <summary>
    ///     Validates a command and throws an exception if validation fails
    /// </summary>
    /// <param name="command">The command to validate</param>
    /// <exception cref="CommandValidationException">Thrown when validation fails</exception>
    public static void ValidateCommandAndThrow(ICommand command)
    {
        var errors = ValidateCommand(command);
        if (errors.Count > 0)
        {
            throw new CommandValidationException(errors);
        }
    }
}
