using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
namespace Sekiban.EventSourcing.WebHelper.Authorizations;

public class AuthorizeDefinitionCollection : IAuthorizeDefinitionCollection
{
    public AuthorizeDefinitionCollection(IEnumerable<IAuthorizeDefinition> collection) =>
        Collection = collection;
    public AuthorizeDefinitionCollection(params IAuthorizeDefinition[] definitions) =>
        Collection = definitions;
    public IEnumerable<IAuthorizeDefinition> Collection
    {
        get;
    }
    public AuthorizeResultType CheckAuthorization(
        AuthorizeMethodType authorizeMethodType,
        ControllerBase controller,
        Type aggregateType,
        Type? commandType,
        IAggregateCommand? command,
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
                        if (httpContext.User.IsInRole(role)) { isInRole = true; }
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
}
