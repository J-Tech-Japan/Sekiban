using Microsoft.Extensions.DependencyInjection;

namespace Sekiban.Infrastructure.IndexedDb;

public class SekibanIndexedDbOptionsServiceCollection(SekibanIndexedDbOptions sekibanIndexedDbOptions, IServiceCollection serviceCollection)
{
    public SekibanIndexedDbOptions SekibanIndexedDbOptions { get; init; } = sekibanIndexedDbOptions;
    public IServiceCollection ServiceCollection { get; init; } = serviceCollection;
}
