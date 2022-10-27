namespace Sekiban.Core.Exceptions;

public class SekibanAggregateCommandNotRegisteredException : Exception, ISekibanException
{

    public SekibanAggregateCommandNotRegisteredException(string commandName)
    {
        CommandName = commandName;
    }
    public string CommandName { get; set; }
}
