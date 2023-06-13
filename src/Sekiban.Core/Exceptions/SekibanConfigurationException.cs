namespace Sekiban.Core.Exceptions;

public class SekibanConfigurationException : Exception, ISekibanException
{

    public SekibanConfigurationException(string? message) : base(message)
    {
    }
}
