using FeatureCheck.Domain.Shared;
using Sekiban.Addon.Web.Authorizations;
using Sekiban.Addon.Web.Authorizations.Definitions;
using Sekiban.Addon.Web.Common;
using Sekiban.Addon.Web.Dependency;
namespace FeatureCheck.WebApi.Dynamo;

public class FeatureCheckWebDependency : FeatureCheckDependency, IWebDependencyDefinition
{
    public AuthorizeDefinitionCollection AuthorizationDefinitions => new(new Allow<AllMethod>());
    public SekibanControllerOptions Options => new();
    public bool ShouldMakeSimpleAggregateListQueries => true;
    public bool ShouldMakeSimpleSingleProjectionListQueries => true;
}
