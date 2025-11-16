using ResultBoxes;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Represents a command that includes its own handler logic.
///     This combines ICommand and ICommandHandler into a single interface,
///     allowing commands to be self-contained with their processing logic.
///     ResultBox-based error handling - returns ResultBox for all operations
/// </summary>
/// <typeparam name="TSelf">The type of the command itself (for CRTP pattern)</typeparam>
public interface ICommandWithHandler<TSelf> : ICommand, ICommandHandler<TSelf> where TSelf : ICommandWithHandler<TSelf>
{
}
