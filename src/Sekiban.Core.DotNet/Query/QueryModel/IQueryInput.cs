namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Query Parameter Interface.
///     Query Developers does not need to implement this interface directly.
/// </summary>
/// <typeparam name="TQueryOutput"></typeparam>
// ReSharper disable once UnusedTypeParameter
public interface IQueryInput<TQueryOutput> : IQueryInputCommon where TQueryOutput : IQueryResponse;
