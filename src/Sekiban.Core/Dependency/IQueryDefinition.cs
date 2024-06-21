namespace Sekiban.Core.Dependency;

/// <summary>
///     System use interface for Query Dependency definition
///     Application developers does not need to implement this interface directly
/// </summary>
public interface IQueryDefinition
{
    public IEnumerable<Type> GetAggregateListQueryTypes();
    public IEnumerable<Type> GetAggregateQueryTypes();
    public IEnumerable<Type> GetSingleProjectionListQueryTypes();
    public IEnumerable<Type> GetSingleProjectionQueryTypes();
    public IEnumerable<Type> GetMultiProjectionQueryTypes();
    public IEnumerable<Type> GetMultiProjectionListQueryTypes();
    public IEnumerable<Type> GetGeneralQueryTypes();
    public IEnumerable<Type> GetGeneralListQueryTypes();

    public IEnumerable<Type> GetNextQueryTypes();
    public IEnumerable<Type> GetNextListQueryTypes();
}
