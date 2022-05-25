using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers;

[Collection("Sequential")]
public abstract class ByTestTestBase : IDisposable
{
    protected readonly IServiceProvider _serviceProvider;
    public ByTestTestBase(bool inMemory) =>
        // ReSharper disable once VirtualMemberCallInConstructor
        _serviceProvider = SetupService(inMemory);
    public void Dispose() { }
    public abstract IServiceProvider SetupService(bool inMemory);
    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn == null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }
}
