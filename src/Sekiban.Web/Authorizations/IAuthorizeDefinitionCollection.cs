using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Command;
namespace Sekiban.Web.Authorizations;

/// <summary>
///     Collection for authorize group definitions
/// </summary>
public interface IAuthorizeDefinitionCollection
{
    IEnumerable<IAuthorizeDefinition> Collection { get; }

    public Task<AuthorizeResultType> CheckAuthorization(
        AuthorizeMethodType authorizeMethodType,
        ControllerBase controller,
        Type aggregateType,
        Type? commandType,
        ICommandCommon? command,
        HttpContext httpContext,
        IServiceProvider serviceProvider);
}
