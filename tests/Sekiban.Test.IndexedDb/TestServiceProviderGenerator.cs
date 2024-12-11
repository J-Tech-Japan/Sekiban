using Microsoft.Extensions.DependencyInjection;
using Sekiban.Infrastructure.IndexedDb;
using Sekiban.Testing.Story;

namespace Sekiban.Test.IndexedDb;

public class TestServiceProviderGenerator : IndexedDbSekibanServiceProviderGenerator, ISekibanServiceProviderGenerator
{
    protected override void AddSekibanJsRuntime(IServiceCollection services)
    {
        services.AddSingleton<ISekibanJsRuntime, NodeJsRuntime>();
    }
}
