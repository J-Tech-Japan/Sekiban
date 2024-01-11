using AspireAndSekibanSample.Domain;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Common;
using Sekiban.Web.Dependency;
namespace AspireAndSekibanSample.ApiService;

public class AspireAndSekibanSampleWebDependency: AspireAndSekibanSampleDomainDependency, IWebDependencyDefinition
{
    public bool ShouldMakeSimpleAggregateListQueries => true;
    public bool ShouldMakeSimpleSingleProjectionListQueries => true;
    public AuthorizeDefinitionCollection AuthorizationDefinitions => new();
    public SekibanControllerOptions Options => new();
}
