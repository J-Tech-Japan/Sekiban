namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     List Query Parameter Interface.
///     All List Query Parameter should implement this interface.
/// </summary>
/// <typeparam name="TOutput"></typeparam>
// ReSharper disable once UnusedTypeParameter
public interface IListQueryInput<out TOutput> : IListQueryInputCommon where TOutput : IQueryResponse;
