using Sekiban.Core.Query.QueryModel;
namespace Sekiban.Testing.Queries;

public interface IQueryChecker
{
    IQueryService? QueryService { get; set; }
}
