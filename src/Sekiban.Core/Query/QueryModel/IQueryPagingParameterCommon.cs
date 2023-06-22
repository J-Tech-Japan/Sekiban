using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Query.QueryModel;

public interface IQueryPagingParameterCommon : IQueryParameterCommon
{
    [Range(1, int.MaxValue)]
    public int? PageSize { get; }

    [Range(1, int.MaxValue)]
    public int? PageNumber { get; }
}
