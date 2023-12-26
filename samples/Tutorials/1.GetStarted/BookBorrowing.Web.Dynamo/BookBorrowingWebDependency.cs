using BookBorrowing.Domain;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Common;
using Sekiban.Web.Dependency;
namespace BookBorrowing.Web.Dynamo;

public class BookBorrowingWebDependency : BookBorrowingDependency, IWebDependencyDefinition
{
    public AuthorizeDefinitionCollection AuthorizationDefinitions => new();
    public SekibanControllerOptions Options => new();

    public bool ShouldMakeSimpleAggregateListQueries => true;
    public bool ShouldMakeSimpleSingleProjectionListQueries => true;
}
