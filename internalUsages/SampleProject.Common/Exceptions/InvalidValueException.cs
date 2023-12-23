namespace ESSampleProjectLib.Exceptions;

public class InvalidValueException : ApplicationException, IValidationNotice
{
    public InvalidValueException(string message) : base(message)
    {
    }
}
