using Sekiban.Dcb.Queries;
namespace Sekiban.Dcb.Orleans.Surrogates;

/// <summary>
///     Orleans surrogate for QueryResultGeneral
/// </summary>
[GenerateSerializer]
public record struct QueryResultGeneralSurrogate(
    [property: Id(0)]
    object Value,
    [property: Id(1)]
    string ResultType,
    [property: Id(2)]
    IQueryCommon Query);
