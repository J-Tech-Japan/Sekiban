using System.ComponentModel.DataAnnotations;

namespace Sekiban.Core.Query.QueryModel.Parameters;

public interface IQueryPagingParameter : IQueryParameter
{
    [Range(1, int.MaxValue)] public int? PageSize { get; }

    [Range(1, int.MaxValue)] public int? PageNumber { get; }
}
