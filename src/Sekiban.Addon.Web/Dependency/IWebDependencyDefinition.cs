using Sekiban.Addon.Web.Authorizations;
using Sekiban.Addon.Web.Common;
using Sekiban.Core.Dependency;
using Sekiban.Core.Query;
namespace Sekiban.Addon.Web.Dependency;

public interface IWebDependencyDefinition : IQueryDefinition
{
    public bool ShouldMakeSimpleAggregateListQueries { get; }

    public bool ShouldMakeSimpleSingleProjectionListQueries { get; }

    // パターン1: Member コマンドだけAdnministrator必要
    //new AuthorizeDefinitionCollection(
    //    // Memberのコマンドに関しては、SiteAdministratorのみを許可する
    //    new AllowOnlyWithRolesAndDenyIfNot<AllCommandsForAggregate<Member>, SomuAppRole>(SomuAppRole.SiteAdministrator),
    //    // その他全てのメソッドはSomuAppUserを許可する
    //    new AllowWithRoles<AllMethod, SomuAppRole>(SomuAppRole.SomuAppUser),
    //    // その他全ては不許可とする
    //    new Deny<AllMethod>()
    //)
    // パターン2: ログインしていれば全てOK
    //new AuthorizeDefinitionCollection(
    //    // 全てのメソッドはログインしていればOK
    //    new AllowIfLoggedIn<AllMethod>()
    //)
    // パターン3: ログインしていなくてもオーケー
    //new AuthorizeDefinitionCollection(new Allow<AllMethod>())
    AuthorizeDefinitionCollection AuthorizationDefinitions { get; }
    public SekibanControllerOptions Options { get; }

    /// <summary>
    ///     コントローラーに表示する集約
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Type> GetAggregatePayloadTypes();

    /// <summary>
    ///     コントローラーに表示する集約
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
            return GetSingleProjectionTypes()
                .Select(
                    m => baseSimpleAggregateListQueryType.MakeGenericType(m));
        }

        return Enumerable.Empty<Type>();
    }

    /// <summary>
    ///     単集約用のプロジェクションリスト
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Type> GetSingleProjectionTypes();

    IEnumerable<(Type serviceType, Type? implementationType)> GetCommandDependencies();
}
