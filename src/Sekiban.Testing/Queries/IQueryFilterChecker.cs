using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Testing.Queries;

public interface IQueryChecker<TProjectionState>
{
    public QueryHandler? QueryHandler { get; set; }
    public void RegisterState(TProjectionState state);
}
