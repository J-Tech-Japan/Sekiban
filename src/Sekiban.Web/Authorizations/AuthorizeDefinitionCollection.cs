using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Command;
using Sekiban.Web.Authorizations.Definitions;
namespace Sekiban.Web.Authorizations;

/// <summary>
///     Authorize definition collection to group authorize definitions
/// </summary>
public class AuthorizeDefinitionCollection : IAuthorizeDefinitionCollection
{

    public static AuthorizeDefinitionCollection AllowAllIfLoggedIn => new(new AllowIfLoggedIn<AllMethod>());
    public static AuthorizeDefinitionCollection AllowAll => new(new Allow<AllMethod>());
    public AuthorizeDefinitionCollection(IEnumerable<IAuthorizeDefinition> collection) => Collection = collection;

    public AuthorizeDefinitionCollection(params IAuthorizeDefinition[] definitions) => Collection = definitions;

    public IEnumerable<IAuthorizeDefinition> Collection { get; set; }

    public AuthorizeResultType CheckAuthorization(
        AuthorizeMethodType authorizeMethodType,
        ControllerBase controller,
        Type aggregateType,
        Type? commandType,
        ICommandCommon? command,
        HttpContext httpContext,
        IServiceProvider serviceProvider)
    {
        foreach (var definition in Collection)
        {
            var result = definition.Check(
                authorizeMethodType,
                aggregateType,
                commandType,
                roles =>
                {
                    var isInRole = false;
                    foreach (var role in roles)
                    {
                        if (httpContext.User.IsInRole(role))
                        {
                            isInRole = true;
                        }
                    }
                    return isInRole;
                },
                httpContext,
                serviceProvider);
            if (result == AuthorizeResultType.Allowed || result == AuthorizeResultType.Denied)
            {
                return result;
            }
        }

        return AuthorizeResultType.Passed;
    }

    public void Add(IAuthorizeDefinition definition)
    {
        Collection = new List<IAuthorizeDefinition>(Collection) { definition };
    }
}
