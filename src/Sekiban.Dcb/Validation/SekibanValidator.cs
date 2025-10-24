using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace Sekiban.Dcb.Validation;

/// <summary>
///     Provides validation functionality for Sekiban objects using DataAnnotations attributes
/// </summary>
public static class SekibanValidator
{
    private const int MaxDepth = 10;

    private static readonly HashSet<string> SystemTypeNamespaces = new()
    {
        "System.Collections.Generic", // Generic collections
        "System.Collections",
        "System.Linq",
        "System.Reflection"
    };

    /// <summary>
    ///     Validates an object using DataAnnotations attributes
    /// </summary>
    /// <param name="obj">The object to validate</param>
    /// <returns>A list of validation errors, empty if valid</returns>
    public static List<SekibanValidationError> Validate(object? obj)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var typePathVisited = new HashSet<string>();
        return ValidateInternal(obj, visited, typePathVisited, string.Empty, 0);
    }

    private static List<SekibanValidationError> ValidateInternal(
        object? obj,
        HashSet<object> visited,
        HashSet<string> typePathVisited,
        string parentPath,
        int depth)
    {
        // Depth limit check
        if (depth >= MaxDepth)
        {
            return new List<SekibanValidationError>();
        }

        if (obj == null)
        {
            // Only return error for top-level null
            if (string.IsNullOrEmpty(parentPath))
            {
                return new List<SekibanValidationError>
                {
                    new("Object", "Object cannot be null")
                };
            }
            return new List<SekibanValidationError>();
        }

        var type = obj.GetType();

        // Skip primitive types, strings, and types that shouldn't be recursively validated
        if (ShouldSkipType(type))
        {
            return new List<SekibanValidationError>();
        }

        // Prevent infinite recursion by tracking visited objects
        if (!visited.Add(obj))
        {
            return new List<SekibanValidationError>();
        }

        // Track type path to detect circular type references
        var typePath = $"{parentPath}:{type.FullName}";
        if (!typePathVisited.Add(typePath))
        {
            return new List<SekibanValidationError>();
        }

        var errors = new List<SekibanValidationError>();
        PropertyInfo[] properties;

        try
        {
            properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }
        catch
        {
            // If we can't get properties, skip this type
            return new List<SekibanValidationError>();
        }

        foreach (var property in properties)
        {
            object? value;
            try
            {
                value = property.GetValue(obj);
            }
            catch
            {
                // If we can't get the value, skip this property
                continue;
            }

            // Try to get validation attributes safely
            IEnumerable<ValidationAttribute> validationAttributes;
            try
            {
                validationAttributes = property.GetCustomAttributes<ValidationAttribute>(true);
            }
            catch
            {
                // If we can't get custom attributes (stack overflow risk), skip validation for this property
                validationAttributes = Enumerable.Empty<ValidationAttribute>();
            }

            foreach (var attribute in validationAttributes)
            {
                try
                {
                    var validationContext = new ValidationContext(obj)
                    {
                        MemberName = property.Name,
                        DisplayName = property.Name
                    };

                    var result = attribute.GetValidationResult(value, validationContext);
                    if (result != ValidationResult.Success && result != null)
                    {
                        var propertyPath = string.IsNullOrEmpty(parentPath)
                            ? property.Name
                            : $"{parentPath}.{property.Name}";
                        errors.Add(
                            new SekibanValidationError(
                                propertyPath,
                                result.ErrorMessage ?? $"Validation failed for {propertyPath}",
                                value));
                    }
                }
                catch (TargetParameterCountException)
                {
                    // Parameter count mismatch - skip this validation attribute
                    continue;
                }
                catch (TargetInvocationException)
                {
                    // Invocation error - skip this validation attribute
                    continue;
                }
            }

            // Recursively validate nested objects
            if (value != null && !ShouldSkipType(value.GetType()))
            {
                var propertyPath = string.IsNullOrEmpty(parentPath)
                    ? property.Name
                    : $"{parentPath}.{property.Name}";
                var nestedErrors = ValidateInternal(value, visited, typePathVisited, propertyPath, depth + 1);
                errors.AddRange(nestedErrors);
            }
        }

        // Remove from type path when leaving this level
        typePathVisited.Remove(typePath);

        return errors;
    }

    private static bool ShouldSkipType(Type type)
    {
        // Skip primitive types and common system types
        if (type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(TimeSpan) ||
            type == typeof(Guid) ||
            type == typeof(decimal) ||
            typeof(Type).IsAssignableFrom(type) ||
            typeof(MemberInfo).IsAssignableFrom(type) ||
            typeof(ParameterInfo).IsAssignableFrom(type) ||
            typeof(Assembly).IsAssignableFrom(type) ||
            typeof(Module).IsAssignableFrom(type))
        {
            return true;
        }

        // Skip types from system namespaces (but not ResultBoxes or user types)
        var typeNamespace = type.Namespace;
        if (typeNamespace != null)
        {
            foreach (var systemNamespace in SystemTypeNamespaces)
            {
                if (typeNamespace.StartsWith(systemNamespace, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        // Skip generic collection types
        if (type.IsGenericType)
        {
            var genericTypeDefinition = type.GetGenericTypeDefinition();
            if (genericTypeDefinition == typeof(List<>) ||
                genericTypeDefinition == typeof(Dictionary<,>) ||
                genericTypeDefinition == typeof(HashSet<>) ||
                genericTypeDefinition == typeof(IEnumerable<>) ||
                genericTypeDefinition == typeof(ICollection<>) ||
                genericTypeDefinition == typeof(IList<>))
            {
                return true;
            }
        }

        return false;
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
