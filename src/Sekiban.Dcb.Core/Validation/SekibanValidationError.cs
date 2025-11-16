namespace Sekiban.Dcb.Validation;

/// <summary>
///     Represents a single validation error for Sekiban objects
/// </summary>
public class SekibanValidationError
{
    public string PropertyName { get; }
    public string ErrorMessage { get; }
    public object? AttemptedValue { get; }

    public SekibanValidationError(string propertyName, string errorMessage, object? attemptedValue = null)
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage;
        AttemptedValue = attemptedValue;
    }

    public override string ToString() => $"{PropertyName}: {ErrorMessage}";
}
