namespace Sekiban.Core.Query.QueryModel;

public interface IQueryInput<TQueryOutput> : IQueryInputCommon where TQueryOutput : IQueryResponse
{
}
