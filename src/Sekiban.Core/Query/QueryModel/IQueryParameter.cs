namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Parameter Interface.
///     Returns single result.
///     Query developers can use this interface to implement Query Parameter.
/// </summary>
/// <typeparam name="TQueryOutput"></typeparam>
public interface IQueryParameter<TQueryOutput> : IQueryParameterCommon, IQueryInput<TQueryOutput> where TQueryOutput : IQueryResponse;
