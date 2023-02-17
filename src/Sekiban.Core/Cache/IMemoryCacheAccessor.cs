using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.Core.Cache;

public interface IMemoryCacheAccessor
{
    IMemoryCache Cache { get; }
}
