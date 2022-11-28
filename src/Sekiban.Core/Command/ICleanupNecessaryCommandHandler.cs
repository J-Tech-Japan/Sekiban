namespace Sekiban.Core.Command;

public interface ICleanupNecessaryCommand<TCommand> : ICommand
{
    TCommand CleanupCommandIfNeeded(TCommand command);
}
