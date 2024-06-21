using Sekiban.Core.Dependency;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Common;
namespace Sekiban.Web.Dependency;

public record GeneralWebDependencyDefinition<TDomainDependencyDefinition> : IWebDependencyDefinition
    where TDomainDependencyDefinition : DomainDependencyDefinitionBase, new()
{
    public TDomainDependencyDefinition DomainDependencyDefinition { get; init; }

    public IEnumerable<Type> AggregateListQueryTypes { get; set; }
    public IEnumerable<Type> AggregateQueryTypes { get; set; }
    public IEnumerable<Type> SingleProjectionListQueryTypes { get; set; }
    public IEnumerable<Type> SingleProjectionQueryTypes { get; set; }
    public IEnumerable<Type> MultiProjectionQueryTypes { get; set; }
    public IEnumerable<Type> MultiProjectionListQueryTypes { get; set; }
    public IEnumerable<Type> GeneralQueryTypes { get; set; }
    public IEnumerable<Type> GeneralListQueryTypes { get; set; }
    public IEnumerable<Type> NextQueryTypes { get; set; }
    public IEnumerable<Type> NextListQueryTypes { get; set; }
    public IEnumerable<Type> AggregatePayloadTypes { get; set; }
    public IEnumerable<Type> AggregatePayloadSubtypes { get; set; }
    public IEnumerable<Type> SingleProjectionTypes { get; set; }
    public IEnumerable<(Type serviceType, Type? implementationType)> CommandDependencies { get; set; }

    public GeneralWebDependencyDefinition()
    {
        DomainDependencyDefinition = new TDomainDependencyDefinition();
        DomainDependencyDefinition.Define();
        AggregateListQueryTypes = DomainDependencyDefinition.GetAggregateListQueryTypes();
        AggregateQueryTypes = DomainDependencyDefinition.GetAggregateQueryTypes();
        SingleProjectionListQueryTypes = DomainDependencyDefinition.GetSingleProjectionListQueryTypes();
        SingleProjectionQueryTypes = DomainDependencyDefinition.GetSingleProjectionQueryTypes();
        MultiProjectionQueryTypes = DomainDependencyDefinition.GetMultiProjectionQueryTypes();
        MultiProjectionListQueryTypes = DomainDependencyDefinition.GetMultiProjectionListQueryTypes();
        GeneralQueryTypes = DomainDependencyDefinition.GetGeneralQueryTypes();
        GeneralListQueryTypes = DomainDependencyDefinition.GetGeneralListQueryTypes();
        AggregatePayloadTypes = DomainDependencyDefinition.GetAggregatePayloadTypes();
        AggregatePayloadSubtypes = DomainDependencyDefinition.GetAggregatePayloadSubtypes();
        SingleProjectionTypes = DomainDependencyDefinition.GetSingleProjectionTypes();
        var commandWithHandler = DomainDependencyDefinition.GetCommandWithHandlerTypes().Select(m => (m, (Type?)m));
        CommandDependencies = DomainDependencyDefinition.GetCommandDependencies().Concat(commandWithHandler);
        NextQueryTypes = DomainDependencyDefinition.GetNextQueryTypes();
        NextListQueryTypes = DomainDependencyDefinition.GetNextListQueryTypes();
    }
    public IEnumerable<Type> GetAggregateListQueryTypes() => AggregateListQueryTypes;
    public IEnumerable<Type> GetAggregateQueryTypes() => AggregateQueryTypes;
    public IEnumerable<Type> GetSingleProjectionListQueryTypes() => SingleProjectionListQueryTypes;
    public IEnumerable<Type> GetSingleProjectionQueryTypes() => SingleProjectionQueryTypes;
    public IEnumerable<Type> GetMultiProjectionQueryTypes() => MultiProjectionQueryTypes;
    public IEnumerable<Type> GetMultiProjectionListQueryTypes() => MultiProjectionListQueryTypes;
    public IEnumerable<Type> GetGeneralQueryTypes() => GeneralQueryTypes;
    public IEnumerable<Type> GetGeneralListQueryTypes() => GeneralListQueryTypes;
    public IEnumerable<Type> GetNextQueryTypes() => NextQueryTypes;
    public IEnumerable<Type> GetNextListQueryTypes() => NextListQueryTypes;
    public bool ShouldMakeSimpleAggregateListQueries { get; set; } = true;
    public bool ShouldMakeSimpleSingleProjectionListQueries { get; set; } = true;
    public bool ShouldAddExceptionFilter { get; set; } = true;
    public AuthorizeDefinitionCollection AuthorizationDefinitions { get; set; } = new();
    public SekibanControllerOptions Options { get; set; } = new();
    public IEnumerable<Type> GetAggregatePayloadTypes() => AggregatePayloadTypes;
    public IEnumerable<Type> GetAggregatePayloadSubtypes() => AggregatePayloadSubtypes;
    public IEnumerable<Type> GetSingleProjectionTypes() => SingleProjectionTypes;
    public IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies() => CommandDependencies;
    public void Define()
    {
    }
    public void AllowAllIfLoggedIn()
    {
        AuthorizationDefinitions = AuthorizeDefinitionCollection.AllowAllIfLoggedIn;
    }
}
