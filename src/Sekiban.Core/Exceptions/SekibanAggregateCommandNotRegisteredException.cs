namespace Sekiban.Core.Exceptions;

public class SekibanAggregateCommandNotRegisteredException : Exception, ISekibanException
{
    public string CommandName { get; set; }

    public SekibanAggregateCommandNotRegisteredException(string commandName)
    {
        CommandName = commandName;
    }
}
