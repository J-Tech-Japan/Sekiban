using System.ComponentModel.DataAnnotations;
namespace Sekiban.Pure.Query;

public interface IQueryPagingParameterCommon
{
    [Range(1, int.MaxValue)]
    public int? PageSize { get; }

    [Range(1, int.MaxValue)]
    public int? PageNumber { get; }
}
