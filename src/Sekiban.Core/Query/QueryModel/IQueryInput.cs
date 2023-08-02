namespace Sekiban.Core.Query.QueryModel;

// ReSharper disable once UnusedTypeParameter
public interface IQueryInput<TQueryOutput> : IQueryInputCommon where TQueryOutput : IQueryResponse;
