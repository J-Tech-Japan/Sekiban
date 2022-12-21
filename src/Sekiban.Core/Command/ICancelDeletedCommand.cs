namespace Sekiban.Core.Command;

/// <summary>
///     Interface for the commands that cancels the deleted state.
///     Please implement this to the Command Payload that cancels the deleted state.
///     Without this Interface, the command throw <see cref="SekibanAggregateAlreadyDeletedException" />
/// </summary>
public interface ICancelDeletedCommand
{
}
