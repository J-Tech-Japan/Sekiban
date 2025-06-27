using Sekiban.Pure.Command.Handlers;

namespace DaprSample.Api;

public class SimpleExecutingUserProvider : IExecutingUserProvider
{
    public string GetExecutingUser() => "system";
}