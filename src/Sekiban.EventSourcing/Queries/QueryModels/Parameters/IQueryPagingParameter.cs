using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.Queries.QueryModels.Parameters;

public interface IQueryPagingParameter : IQueryParameter
{
    [Range(1, int.MaxValue)]
    public int? PageSize { get; }
    [Range(1, int.MaxValue)]
    public int? PageNumber { get; }
}
