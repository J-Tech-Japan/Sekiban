using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Testing.QueryFilter;

public interface IQueryFilterChecker<TProjectionState>
{
    public QueryFilterHandler? QueryFilterHandler { get; set; }
    public void RegisterState(TProjectionState state);
}
