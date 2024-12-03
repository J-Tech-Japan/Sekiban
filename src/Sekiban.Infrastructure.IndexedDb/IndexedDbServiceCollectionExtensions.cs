using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.IndexedDb.Databases;
using Sekiban.Infrastructure.IndexedDb.Documents;

namespace Sekiban.Infrastructure.IndexedDb;

/// <summary>
/// Add IndexedDB Services for Sekiban
/// </summary>
public static class IndexedDbServiceCollectionExtensions
{
    public static SekibanIndexedDbOptionsServiceCollection AddSekibanIndexedDb(this IServiceCollection services, IConfiguration configuration)
    {
        var options = SekibanIndexedDbOptions.FromConfiguration(configuration);
        return AddSekibanIndexedDbWithoutBlob(services, options);
    }

    public static SekibanIndexedDbOptionsServiceCollection AddSekibanIndexedDbWithoutBlob(
        this IServiceCollection services,
        SekibanIndexedDbOptions indexedDbOptions
    )
    {
        services.AddSingleton(indexedDbOptions);

        services.AddTransient<IndexedDbFactory>();

        services.AddTransient<IDocumentPersistentWriter, IndexedDbDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, IndexedDbDocumentRepository>();

        services.AddTransient<IEventPersistentWriter, IndexedDbDocumentWriter>();
        services.AddTransient<IEventPersistentRepository, IndexedDbDocumentRepository>();

        services.AddTransient<IDocumentRemover, IndexedDbDocumentRemover>();

        return new SekibanIndexedDbOptionsServiceCollection(indexedDbOptions, services);
    }
}
