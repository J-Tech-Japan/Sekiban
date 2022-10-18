using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Testing.QueryFilter;

public interface IQueryFilterChecker<TProjectionDto>
{
    public QueryFilterHandler? QueryFilterHandler { get; set; }
    public void RegisterDto(TProjectionDto dto);
}
