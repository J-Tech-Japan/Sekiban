namespace Sekiban.EventSourcing.AggregateCommands.UserInformations;

/// <summary>
///     こちらは定数をユーザー名、ユーザーIDとして返します。
///     WebApiのプロジェクトでは、
/// </summary>
public class ConstUserInformationFactory : IUserInformationFactory
{
    private readonly string _userInfo;
    public ConstUserInformationFactory(string userInfo)
    {
        _userInfo = userInfo;
    }

    public string GetCurrentUserInformation()
    {
        return _userInfo ?? string.Empty;
    }
}
