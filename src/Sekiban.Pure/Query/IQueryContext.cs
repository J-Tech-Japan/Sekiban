using ResultBoxes;
namespace Sekiban.Pure.Query;

public interface IQueryContext
{
    ResultBox<T> GetService<T>() where T : notnull;
}