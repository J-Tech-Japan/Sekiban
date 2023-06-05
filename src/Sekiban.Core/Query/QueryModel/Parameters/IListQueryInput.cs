namespace Sekiban.Core.Query.QueryModel.Parameters;

public interface IListQueryInput<out TOutput> : IListQueryInputCommon where TOutput : IQueryResponse
{
}
