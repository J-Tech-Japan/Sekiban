namespace Sekiban.Core.Query.QueryModel.Parameters;

// ReSharper disable once UnusedTypeParameter
public interface IListQueryInput<out TOutput> : IListQueryInputCommon where TOutput : IQueryResponse
{
}
