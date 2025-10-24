using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace Sekiban.Dcb.Validation;

/// <summary>
///     Provides validation functionality for Sekiban objects using DataAnnotations attributes
/// </summary>
public static class SekibanValidator
{
    /// <summary>
    ///     Validates an object using DataAnnotations attributes
    /// </summary>
    /// <param name="obj">The object to validate</param>
    /// <returns>A list of validation errors, empty if valid</returns>
    public static List<SekibanValidationError> Validate(object? obj)
    {
        if (obj == null)
        {
            return new List<SekibanValidationError>
            {
                new("Object", "Object cannot be null")
            };
        }

        var errors = new List<SekibanValidationError>();
        var type = obj.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var value = property.GetValue(obj);
            var validationAttributes = property.GetCustomAttributes<ValidationAttribute>(true);

            foreach (var attribute in validationAttributes)
            {
                var validationContext = new ValidationContext(obj)
                {
                    MemberName = property.Name,
                    DisplayName = property.Name
                };

                var result = attribute.GetValidationResult(value, validationContext);
                if (result != ValidationResult.Success && result != null)
                {
                    errors.Add(
                        new SekibanValidationError(
                            property.Name,
                            result.ErrorMessage ?? $"Validation failed for {property.Name}",
                            value));
                }
            }

            // Also validate nested complex objects recursively
            // Skip primitive types and strings to avoid infinite recursion
            if (value != null && !value.GetType().IsPrimitive && value is not string)
            {
                var nestedErrors = Validate(value);
                foreach (var nestedError in nestedErrors)
                {
                    errors.Add(
                        new SekibanValidationError(
                            $"{property.Name}.{nestedError.PropertyName}",
                            nestedError.ErrorMessage,
                            nestedError.AttemptedValue));
                }
            }
        }

        return errors;
    }

    /// <summary>
    ///     Validates an object and throws an exception if validation fails
    /// </summary>
    /// <param name="obj">The object to validate</param>
    /// <exception cref="SekibanValidationException">Thrown when validation fails</exception>
    public static void ValidateAndThrow(object? obj)
    {
        var errors = Validate(obj);
        if (errors.Count > 0)
        {
            throw new SekibanValidationException(errors);
        }
    }
}
