namespace Sekiban.EventSourcing.Shared.Exceptions;

public class SekibanCanNotExecuteRegularChangeCommandException : Exception, ISekibanException
{
    public SekibanCanNotExecuteRegularChangeCommandException(string commandName) : base($"Can not execute regular command: {commandName}")
    {
    }
}