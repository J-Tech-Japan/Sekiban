using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Sekiban.Testing.Story;

[Collection("Sequential")]
public abstract class SekibanByTestTestBase : IDisposable
{
    protected IServiceProvider ServiceProvider { get; set; } = default!;

    public void Dispose()
    {
    }


    public T GetService<T>()
    {
        var toreturn = ServiceProvider.GetService<T>();
        if (toreturn is null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }
}
