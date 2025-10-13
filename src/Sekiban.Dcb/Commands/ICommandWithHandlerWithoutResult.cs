namespace Sekiban.Dcb.Commands;

/// <summary>
///     Command that contains its own handler logic without using ResultBox.
/// </summary>
/// <typeparam name="TSelf">Self type constraint.</typeparam>
public interface ICommandWithHandlerWithoutResult<TSelf> : ICommand, ICommandHandlerWithoutResult<TSelf>
    where TSelf : ICommandWithHandlerWithoutResult<TSelf>
{
}
