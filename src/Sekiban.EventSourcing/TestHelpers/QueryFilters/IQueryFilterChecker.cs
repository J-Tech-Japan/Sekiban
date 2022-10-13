using Sekiban.EventSourcing.Queries.QueryModels;
namespace Sekiban.EventSourcing.TestHelpers.QueryFilters;

public interface IQueryFilterChecker<TProjectionDto>
{
    public QueryFilterHandler? QueryFilterHandler { get; set; }
    public void RegisterDto(TProjectionDto dto);
}
