namespace Sekiban.Core.Command.UserInformation;

/// <summary>
///     This class returns constants as the username and userID.
/// </summary>
public class ConstUserInformationFactory : IUserInformationFactory
{
    private readonly string _userInfo;

    public ConstUserInformationFactory(string userInfo) => _userInfo = userInfo;

    public string GetCurrentUserInformation() => _userInfo;
}
