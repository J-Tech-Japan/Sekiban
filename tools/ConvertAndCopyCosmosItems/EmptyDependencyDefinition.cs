using Sekiban.Core.Dependency;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Common;
using Sekiban.Web.Dependency;
using System.Reflection;
namespace Convert011To012;

public class EmptyDependencyDefinition : DomainDependencyDefinitionBase, IWebDependencyDefinition
{
    public bool ShouldMakeSimpleAggregateListQueries => true;
    public bool ShouldMakeSimpleSingleProjectionListQueries => true;
    public new bool ShouldAddExceptionFilter => true;
    public AuthorizeDefinitionCollection AuthorizationDefinitions => new();
    public SekibanControllerOptions Options => new();
    public override void Define()
    {
    }

    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
}
