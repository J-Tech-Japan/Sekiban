namespace Sekiban.EventSourcing.AggregateCommands;

/// <summary>
///     こちらは定数をユーザー名、ユーザーIDとして返します。
///     WebApiのプロジェクトでは、
/// </summary>
public class ConstUserInformationFactory : IUserInformationFactory
{
    private readonly string _userInfo;
    public ConstUserInformationFactory(string userInfo) =>
        _userInfo = userInfo;

    public string GetCurrentUserInformation() =>
        _userInfo ?? string.Empty;
}
