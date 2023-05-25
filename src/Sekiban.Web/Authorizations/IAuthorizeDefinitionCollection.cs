using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Command;
namespace Sekiban.Web.Authorizations;

public interface IAuthorizeDefinitionCollection
{
    IEnumerable<IAuthorizeDefinition> Collection { get; }

    public AuthorizeResultType CheckAuthorization(
        AuthorizeMethodType authorizeMethodType,
        ControllerBase controller,
        Type aggregateType,
        Type? commandType,
        ICommandCommon? command,
        HttpContext httpContext,
        IServiceProvider serviceProvider);
}
