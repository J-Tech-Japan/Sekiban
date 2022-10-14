using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Sekiban.EventSourcing.TestHelpers.StoryTests;

[Collection("Sequential")]
public abstract class SekibanByTestTestBase : IDisposable
{
    protected readonly IServiceProvider _serviceProvider;
    public SekibanByTestTestBase(bool inMemory)
    {
        // ReSharper disable once VirtualMemberCallInConstructor
        _serviceProvider = SetupService(inMemory);
    }
    public void Dispose() { }
    public abstract IServiceProvider SetupService(bool inMemory);
    public T GetService<T>()
    {
        var toreturn = _serviceProvider.GetService<T>();
        if (toreturn is null)
        {
            throw new Exception("オブジェクトが登録されていません。" + typeof(T));
        }
        return toreturn;
    }
}
