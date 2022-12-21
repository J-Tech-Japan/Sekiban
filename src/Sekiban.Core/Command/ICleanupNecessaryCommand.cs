namespace Sekiban.Core.Command;

/// <summary>
///     Interface for convert command for the persistence.
///     Some cases, the "Raw" command is not suitable for the persistence.
///     e.g. security reason (password), or the command is too long.
///     Application developer can implement this interface to convert the command to the suitable one.
/// </summary>
/// <typeparam name="TCommand"></typeparam>
public interface ICleanupNecessaryCommand<TCommand> : ICommandCommon
{
    /// <summary>
    ///     Convert the command to the suitable one for the persistence.
    /// </summary>
    /// <param name="command">Raw command used for the aggregate</param>
    /// <returns>Converted (Cleaned) command for the persistence</returns>
    TCommand CleanupCommand(TCommand command);
}
