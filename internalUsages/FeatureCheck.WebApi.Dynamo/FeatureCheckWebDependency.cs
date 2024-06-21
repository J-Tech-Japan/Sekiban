using FeatureCheck.Domain.Shared;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Authorizations.Definitions;
using Sekiban.Web.Common;
using Sekiban.Web.Dependency;
namespace FeatureCheck.WebApi.Dynamo;

public class FeatureCheckWebDependency : FeatureCheckDependency, IWebDependencyDefinition
{
    public IAuthorizeDefinitionCollection AuthorizationDefinitions => new AuthorizeDefinitionCollection(new Allow<AllMethod>());
    public SekibanControllerOptions Options => new();
    public bool ShouldMakeSimpleAggregateListQueries => true;
    public bool ShouldMakeSimpleSingleProjectionListQueries => true;
}
