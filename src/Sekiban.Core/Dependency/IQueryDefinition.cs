namespace Sekiban.Core.Dependency;

public interface IQueryDefinition
{
    public IEnumerable<Type> GetAggregateListQueryTypes();
    public IEnumerable<Type> GetAggregateQueryTypes();
    public IEnumerable<Type> GetSingleProjectionListQueryTypes();
    public IEnumerable<Type> GetSingleProjectionQueryTypes();
    public IEnumerable<Type> GetMultiProjectionQueryTypes();
    public IEnumerable<Type> GetMultiProjectionListQueryTypes();
}
