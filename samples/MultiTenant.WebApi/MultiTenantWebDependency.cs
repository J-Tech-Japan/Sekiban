using MultiTenant.Domain.Aggregates;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Authorizations.Definitions;
using Sekiban.Web.Common;
using Sekiban.Web.Dependency;
namespace MultiTenant.WebApi;

public class MultiTenantWebDependency : MultiTenantDependency, IWebDependencyDefinition
{
    public AuthorizeDefinitionCollection AuthorizationDefinitions => new(new Allow<AllMethod>());

    public bool ShouldMakeSimpleAggregateListQueries => true;
    public bool ShouldMakeSimpleSingleProjectionListQueries => true;
    public SekibanControllerOptions Options => new();
}
