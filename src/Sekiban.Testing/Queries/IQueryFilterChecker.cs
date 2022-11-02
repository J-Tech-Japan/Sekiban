using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Testing.Queries;

public interface IQueryTest
{
    IQueryService? QueryService { get; set; }
}
