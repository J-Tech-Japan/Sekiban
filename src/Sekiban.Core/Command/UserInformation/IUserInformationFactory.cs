namespace Sekiban.Core.Command.UserInformation;

/// <summary>
///     Get user information. Use for command logging.
///     Collect executing user through context using this interface.
/// </summary>
public interface IUserInformationFactory
{
    /// <summary>
    ///     Get user information
    /// </summary>
    /// <returns>user information string.</returns>
    string GetCurrentUserInformation();
}
