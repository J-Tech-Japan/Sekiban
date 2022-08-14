using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
namespace Sekiban.EventSourcing.WebHelper.Authorizations;

public interface IAuthorizeDefinitionCollection
{
    IEnumerable<IAuthorizeDefinition> Collection { get; }

    public AuthorizeResultType CheckAuthorization(
        AuthorizeMethodType authorizeMethodType,
        ControllerBase controller,
        Type aggregateType,
        Type? commandType,
        IAggregateCommand? command,
        HttpContext httpContext);
}
