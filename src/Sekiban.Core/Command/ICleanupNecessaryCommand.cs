namespace Sekiban.Core.Command;

public interface ICleanupNecessaryCommand<TCommand> : ICommandCommon
{
    TCommand CleanupCommand(TCommand command);
}
