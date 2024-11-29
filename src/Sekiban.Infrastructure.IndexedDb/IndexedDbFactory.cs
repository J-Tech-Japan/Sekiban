using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Cache;
using Sekiban.Core.Setting;

namespace Sekiban.Infrastructure.IndexedDb;

public class IndexedDbFactory(
    SekibanIndexedDbOptions options,
    IMemoryCacheAccessor memoryCache,
    IServiceProvider serviceProvider
)
{
    private string SekibanContextIdentifier()
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }
}
