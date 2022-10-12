namespace Sekiban.EventSourcing.Shared.Exceptions;

public class SekibanCanNotExecuteOnlyPublishingEventCommand : Exception, ISekibanException
{
    public SekibanCanNotExecuteOnlyPublishingEventCommand(string commandName) : base($"Can not execute only publishing event command: {commandName}")
    {
    }
}
