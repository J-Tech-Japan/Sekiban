using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Command;
using Sekiban.Web.Authorizations.Definitions;
using System.Security.Claims;
namespace Sekiban.Web.Authorizations;

public class
    AuthorizeDefinitionCollectionWithUserManager<TIdentity> : IAuthorizeDefinitionCollection
    where TIdentity : class
{
    public AuthorizeDefinitionCollectionWithUserManager(
        IEnumerable<IAuthorizeDefinition> collection) => Collection = collection;

    public AuthorizeDefinitionCollectionWithUserManager(
        params IAuthorizeDefinition[] definitions) => Collection = definitions;

    public static AuthorizeDefinitionCollection AllowAllIfLoggedIn =>
        new(new AllowIfLoggedIn<AllMethod>());
    public static AuthorizeDefinitionCollection AllowAll => new(new Allow<AllMethod>());

    public IEnumerable<IAuthorizeDefinition> Collection { get; set; }

    public async Task<AuthorizeResultType> CheckAuthorization(
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
            var result = await definition.Check(
                authorizeMethodType,
                aggregateType,
                commandType,
                async roles =>
                {
                    var isInRole = false;

                    var userIdClaim =
                        httpContext.User.Claims.FirstOrDefault(
                            c => c.Type == ClaimTypes.NameIdentifier);
                    if (userIdClaim == null)
                    {
                        return isInRole;
                    }
                    var userManager = serviceProvider.GetRequiredService<UserManager<TIdentity>>();

                    var user = await userManager.FindByIdAsync(userIdClaim.Value);
                    if (user == null)
                    {
                        return isInRole;
                    }


                    foreach (var role in roles)
                    {
                        if (await userManager.IsInRoleAsync(user, role))
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
