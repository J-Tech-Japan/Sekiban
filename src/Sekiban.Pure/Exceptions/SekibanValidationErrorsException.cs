using Sekiban.Pure.Exceptions;
using Sekiban.Pure.Validations;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Pure.Exceptions;

/// <summary>
///     This exception is thrown when the validation errors are found.
/// </summary>
// ReSharper disable once ParameterTypeCanBeEnumerable.Local
public class SekibanValidationErrorsException(List<SekibanValidationParameterError> errors) : Exception(
        errors
            .Select(e => e.PropertyName + ":" + e.ErrorMessages.Aggregate("", (s, s1) => s + s1))
            .Aggregate("", (s, s1) => s + "\n" + s1)),
    ISekibanException
{
    public List<SekibanValidationParameterError> Errors => errors;
    public SekibanValidationErrorsException(IEnumerable<ValidationResult> validationResults) : this(
        SekibanValidationParameterError.CreateFromValidationResults(validationResults).ToList())
    {
    }
}