using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Query.QueryModel;

/// <summary>
///     Paging Query Parameter Interface.
///     Query developer does not need to implement this interface directly.
/// </summary>
public interface IQueryPagingParameterCommon : IQueryParameterCommon
{
    [Range(1, int.MaxValue)]
    public int? PageSize { get; }

    [Range(1, int.MaxValue)]
    public int? PageNumber { get; }
}
