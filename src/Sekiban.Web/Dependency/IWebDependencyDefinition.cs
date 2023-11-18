using Sekiban.Core.Dependency;
using Sekiban.Core.Query;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Common;
namespace Sekiban.Web.Dependency;

/// <summary>
///     Web Dependency Definition
/// </summary>
public interface IWebDependencyDefinition : IQueryDefinition
{
    public bool ShouldMakeSimpleAggregateListQueries { get; }

    public bool ShouldMakeSimpleSingleProjectionListQueries { get; }

    public bool ShouldAddExceptionFilter { get; }

    // Pattern 1: Only Member command requires Administrator.
    //new AuthorizeDefinitionCollection(
    //    // For Member commands, only allow SiteAdministrator.
    //    new AllowOnlyWithRolesAndDenyIfNot<AllCommandsForAggregate<Member>, SomuAppRole>(SomuAppRole.SiteAdministrator),
    //    // All other methods allow SomuAppUser.
    //    new AllowWithRoles<AllMethod, SomuAppRole>(SomuAppRole.SomuAppUser),
    //    // All other situations are not allowed.
    //    new Deny<AllMethod>()
    //)
    // Pattern 2: Everything is OK if logged in.
    //new AuthorizeDefinitionCollection(
    //    // All methods are OK if logged in.
    //    new AllowIfLoggedIn<AllMethod>()
    //)
    // Pattern 3: OK even if not logged in.
    //new AuthorizeDefinitionCollection(new Allow<AllMethod>())
    AuthorizeDefinitionCollection AuthorizationDefinitions { get; }
    public SekibanControllerOptions Options { get; }

    /// <summary>
    ///     Get Aggregate Payload Types
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Type> GetAggregatePayloadTypes();

    /// <summary>
    ///     Get Aggregate Payload Subtypes
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Type> GetAggregatePayloadSubtypes();

    public IEnumerable<Type> GetSimpleAggregateListQueryTypes()
    {
        if (ShouldMakeSimpleAggregateListQueries)
        {
            var baseSimpleAggregateListQueryType = typeof(SimpleAggregateListQuery<>);
            return GetAggregatePayloadTypes().Select(m => baseSimpleAggregateListQueryType.MakeGenericType(m));
        }

        return Enumerable.Empty<Type>();
    }

    public IEnumerable<Type> GetSimpleSingleProjectionListQueryTypes()
    {
        if (ShouldMakeSimpleSingleProjectionListQueries)
        {
            var baseSimpleAggregateListQueryType = typeof(SimpleSingleProjectionListQuery<>);
            return GetSingleProjectionTypes().Select(m => baseSimpleAggregateListQueryType.MakeGenericType(m));
        }

        return Enumerable.Empty<Type>();
    }

    /// <summary>
    ///     Get Single Projection Types
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Type> GetSingleProjectionTypes();

    IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies();

    public void Define();
}
