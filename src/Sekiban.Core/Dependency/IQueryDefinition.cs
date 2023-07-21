using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Core.Dependency;

/// <summary>
///     System use interface for Query Dependency definition
///     Application developers does not need to implement this interface directly
/// </summary>
public interface IQueryDefinition
{
    /// <summary>
    ///     Queries that uses Aggregate List and return list object
    ///     should inherit from <see cref="IAggregateListQuery{TAggregatePayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Type> GetAggregateListQueryTypes();
    /// <summary>
    ///     Queries that uses Aggregate List and return single object
    ///     should inherit from <see cref="IAggregateQuery{TAggregatePayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    public IEnumerable<Type> GetAggregateQueryTypes();
    /// <summary>
    ///     Queries that uses Aggregate List and return list object
    ///     should inherit from <see cref="ISingleProjectionListQuery{TAggregatePayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    public IEnumerable<Type> GetSingleProjectionListQueryTypes();
    /// <summary>
    ///     Queries that uses Single Projection List and return single object
    ///     should inherit from <see cref="ISingleProjectionQuery{TAggregatePayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    public IEnumerable<Type> GetSingleProjectionQueryTypes();
    /// <summary>
    ///     Queries that uses Multi Projection and return list object
    ///     should inherit from <see cref="IMultiProjectionListQuery{TProjectionPayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    public IEnumerable<Type> GetMultiProjectionQueryTypes();
    /// <summary>
    ///     Queries that uses Multi Projection and return single object
    ///     should inherit from <see cref="IMultiProjectionQuery{TProjectionPayload,TQueryParameter,TQueryResponse}" />
    /// </summary>
    public IEnumerable<Type> GetMultiProjectionListQueryTypes();
    /// <summary>
    ///     Queries that uses not specific data and return single object
    ///     data should be retrieve in the methods
    ///     should inherit from <see cref="IGeneralQuery{TQueryParameter,TQueryResponse}" />
    /// </summary>
    public IEnumerable<Type> GetGeneralQueryTypes();
    /// <summary>
    ///     Queries that uses not specific data and return list object
    ///     data should be retrieve in the methods
    ///     should inherit from <see cref="IGeneralQuery{TQueryParameter,TQueryResponse}" />
    /// </summary>
    public IEnumerable<Type> GetGeneralListQueryTypes();
}
