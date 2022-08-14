using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
namespace Sekiban.EventSourcing.WebHelper.Authorizations;

public class AuthorizeDefinitionCollection : IAuthorizeDefinitionCollection
{
    public AuthorizeDefinitionCollection(IEnumerable<IAuthorizeDefinition> collection) =>
        Collection = collection;
    public AuthorizeDefinitionCollection(IAuthorizeDefinition definition)
    {
        Collection = new[] { definition };
    }
    public AuthorizeDefinitionCollection(IAuthorizeDefinition definition1, IAuthorizeDefinition definition2)
    {
        Collection = new[] { definition1, definition2 };
    }
    public AuthorizeDefinitionCollection(IAuthorizeDefinition definition1, IAuthorizeDefinition definition2, IAuthorizeDefinition definition3)
    {
        Collection = new[] { definition1, definition2, definition3 };
    }
    public AuthorizeDefinitionCollection(
        IAuthorizeDefinition definition1,
        IAuthorizeDefinition definition2,
        IAuthorizeDefinition definition3,
        IAuthorizeDefinition definition4)
    {
        Collection = new[] { definition1, definition2, definition3, definition4 };
    }
    public AuthorizeDefinitionCollection(
        IAuthorizeDefinition definition1,
        IAuthorizeDefinition definition2,
        IAuthorizeDefinition definition3,
        IAuthorizeDefinition definition4,
        IAuthorizeDefinition definition5)
    {
        Collection = new[]
        {
            definition1,
            definition2,
            definition3,
            definition4,
            definition5
        };
    }
    public AuthorizeDefinitionCollection(
        IAuthorizeDefinition definition1,
        IAuthorizeDefinition definition2,
        IAuthorizeDefinition definition3,
        IAuthorizeDefinition definition4,
        IAuthorizeDefinition definition5,
        IAuthorizeDefinition definition6)
    {
        Collection = new[]
        {
            definition1,
            definition2,
            definition3,
            definition4,
            definition5,
            definition6
        };
    }
    public AuthorizeDefinitionCollection(
        IAuthorizeDefinition definition1,
        IAuthorizeDefinition definition2,
        IAuthorizeDefinition definition3,
        IAuthorizeDefinition definition4,
        IAuthorizeDefinition definition5,
        IAuthorizeDefinition definition6,
        IAuthorizeDefinition definition7)
    {
        Collection = new[]
        {
            definition1,
            definition2,
            definition3,
            definition4,
            definition5,
            definition6,
            definition7
        };
    }
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
