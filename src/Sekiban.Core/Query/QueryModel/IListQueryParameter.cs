namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Interface for List Query Parameter.
/// </summary>
/// <typeparam name="TQueryOutput"></typeparam>
public interface IListQueryParameter<TQueryOutput> : IQueryParameterCommon, IListQueryInput<TQueryOutput> where TQueryOutput : IQueryResponse;
