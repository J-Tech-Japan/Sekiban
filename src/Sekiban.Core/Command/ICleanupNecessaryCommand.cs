namespace Sekiban.Core.Command;

public interface ICleanupNecessaryCommand<TCommand> : ICommandCommon
{
    TCommand CleanupCommandIfNeeded(TCommand command);
}
