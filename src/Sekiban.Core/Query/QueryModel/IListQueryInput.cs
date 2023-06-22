namespace Sekiban.Core.Query.QueryModel;

// ReSharper disable once UnusedTypeParameter
public interface IListQueryInput<out TOutput> : IListQueryInputCommon where TOutput : IQueryResponse
{
}
