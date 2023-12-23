using Sekiban.Web.Authorizations;
using Sekiban.Web.Common;
using Sekiban.Web.Dependency;
using SekibanEventSourcingBasics.Domain;
namespace SekibanEventSourcingBasics.Cosmos.Web;

public class SekibanWebDependency : DomainDependency, IWebDependencyDefinition
{
    public bool ShouldMakeSimpleAggregateListQueries => true;
    public bool ShouldMakeSimpleSingleProjectionListQueries => true;
    public AuthorizeDefinitionCollection AuthorizationDefinitions => new();
    public SekibanControllerOptions Options => new();
}
