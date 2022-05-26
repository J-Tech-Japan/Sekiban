namespace Sekiban.EventSourcing.Shared.Exceptions;

public class SekibanAggregateCommandNotRegisteredException : Exception, ISekibanException
{
    public string CommandName { get; set; }

    public SekibanAggregateCommandNotRegisteredException(string commandName) =>
        CommandName = commandName;
}
