using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Sekiban.Testing.Story;

/// <summary>
///     Base class for Sekiban Test
/// </summary>
[Collection("Sequential")]
public abstract class SekibanByTestTestBase : IDisposable
{
    /// <summary>
    ///     Service Provider
    /// </summary>
    protected IServiceProvider ServiceProvider { get; set; } = default!;

    public void Dispose()
    {
    }

    /// <summary>
    ///     Get Specific Service. If the service is not registered, throw Exception.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public T GetService<T>()
    {
        var toreturn = ServiceProvider.GetService<T>() ?? throw new Exception("The object has not been registered." + typeof(T));
        return toreturn;
    }
}
