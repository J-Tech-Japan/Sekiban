namespace Sekiban.Core.Query.QueryModel;

public interface IQueryParameter<TQueryOutput> : IQueryParameterCommon, IQueryInput<TQueryOutput> where TQueryOutput : IQueryResponse;
