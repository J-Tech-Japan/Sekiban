using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
namespace Sekiban.Pure.Query;

public class QueryContext(IServiceProvider serviceProvider) : IQueryContext
{
    public ResultBox<T> GetService<T>() where T : notnull => ResultBox.CheckNull(serviceProvider.GetService<T>());
}