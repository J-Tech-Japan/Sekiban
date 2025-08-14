namespace Sekiban.Dcb.Validation;

/// <summary>
/// Represents a single command validation error
/// </summary>
public class CommandValidationError
{
    public string PropertyName { get; }
    public string ErrorMessage { get; }
    public object? AttemptedValue { get; }

    public CommandValidationError(string propertyName, string errorMessage, object? attemptedValue = null)
    {
        PropertyName = propertyName;
        ErrorMessage = errorMessage;
        AttemptedValue = attemptedValue;
    }

    public override string ToString() => $"{PropertyName}: {ErrorMessage}";
}