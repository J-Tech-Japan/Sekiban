using Sekiban.Pure.Exceptions;
using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
namespace Sekiban.Pure.Validations;

/// <summary>
///     Validation Extensions for Sekiban.
/// </summary>
public static class ValidationExtensions
{
    /// <summary>
    ///     validate properties.
    /// </summary>
    /// <param name="collection"></param>
    /// <returns></returns>
    public static IEnumerable<(int, ValidationResult)> ValidateEnumerable(this IEnumerable collection)
    {
        var validationResults = new List<ValidationResult>();
        var list = collection.Cast<object>().ToList();
        foreach (var (item, index) in list.Select((item, index) => (item, index)))
        {
            if (item is null)
            {
                continue;
            }
            validationResults.Clear();
            validationResults.AddRange(item.ValidateProperties());
            foreach (var validationResult in validationResults)
            {
                yield return (index, validationResult);
            }
        }
    }
    /// <summary>
    ///     validate properties.
    /// </summary>
    /// <param name="collection"></param>
    /// <param name="validationResults"></param>
    /// <returns></returns>
    public static bool TryValidateEnumerable(
        this IEnumerable collection,
        out IEnumerable<(int, ValidationResult)> validationResults)
    {
        validationResults = collection.ValidateEnumerable();
        return !validationResults.Any();
    }
    /// <summary>
    ///     Validate specific type of properties.
    /// </summary>
    /// <param name="collection"></param>
    /// <param name="validationResults"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static bool TryValidateEnumerable<T>(
        this IEnumerable<T> collection,
        out IEnumerable<(int, ValidationResult)> validationResults)
    {
        validationResults = collection.ValidateEnumerable();
        return !validationResults.Any();
    }
    /// <summary>
    ///     Validate specific type of properties.
    /// </summary>
    /// <param name="collection"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static IEnumerable<(int, ValidationResult)> ValidateEnumerable<T>(this IEnumerable<T> collection)
    {
        var validationResults = new List<ValidationResult>();
        var list = collection.ToList();
        foreach (var (item, index) in list.Select((item, index) => (item, index)))
        {
            if (item is null)
            {
                continue;
            }
            validationResults.Clear();
            validationResults.AddRange(item.ValidateProperties());
            foreach (var validationResult in validationResults)
            {
                yield return (index, validationResult);
            }
        }
    }
    /// <summary>
    ///     Validate properties.
    /// </summary>
    /// <param name="targetClass"></param>
    /// <param name="validationResults"></param>
    /// <param name="baseKeyPath"></param>
    /// <returns></returns>
    public static bool TryValidateProperties(
        this object targetClass,
        out IEnumerable<ValidationResult> validationResults,
        string baseKeyPath = "")
    {
        validationResults = targetClass.ValidateProperties(baseKeyPath);
        return !validationResults.Any();
    }

    public static void ThrowIfValidationFailed(this object targetClass, string baseKeyPath = "")
    {
        var validationResults = targetClass.ValidateProperties(baseKeyPath).ToList();
        if (validationResults.Count == 0)
        {
            return;
        }
        throw new SekibanValidationErrorsException(validationResults);
    }

    public static IEnumerable<ValidationResult> ValidateProperties(this object targetClass, string baseKeyPath = "")
    {
        var validationResults = new List<ValidationResult>();

        // In case the object is a collection.
        if (targetClass is IEnumerable collection)
        {
            foreach (var (index, validationResult) in collection.ValidateEnumerable())
            {
                yield return new ValidationResult(
                    validationResult.ErrorMessage,
                    validationResult
                        .MemberNames
                        .Select(m => string.IsNullOrEmpty(baseKeyPath) ? m : $"{baseKeyPath}[{index}].{m}")
                        .ToArray());
            }

            yield break;
        }

        // General property validation.
        Validator.TryValidateObject(targetClass, new ValidationContext(targetClass), validationResults, true);
        foreach (var validationResult in validationResults)
        {
            yield return new ValidationResult(
                validationResult.ErrorMessage,
                validationResult
                    .MemberNames
                    .Select(m => string.IsNullOrEmpty(baseKeyPath) ? m : $"{baseKeyPath}.{m}")
                    .ToArray());
        }

        static bool isReferenceType(Type type)
        {
            return type switch
            {
                { IsPrimitive: true } => false,
                { IsEnum: true } => false,
                { IsGenericType: true } t when t.GetGenericTypeDefinition() == typeof(Nullable<>) => false,
                { } t when t == typeof(string) => false,
                { } t when t == typeof(DateTime) => false,
                _ => true
            };
        }

        // Check whether there is a reference type property.
        foreach (var pi in targetClass.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!isReferenceType(pi.PropertyType))
            {
                continue;
            }

            if (pi.PropertyType == typeof(Type))
            {
                continue;
            }

            var pvalue = pi.GetValue(targetClass);
            if (pvalue is null)
            {
                continue;
            }

            validationResults.Clear();
            validationResults.AddRange(
                pvalue.ValidateProperties(string.IsNullOrEmpty(baseKeyPath) ? pi.Name : $"{baseKeyPath}.{pi.Name}"));
            foreach (var validationResult in validationResults)
            {
                yield return validationResult;
            }
        }
    }
}