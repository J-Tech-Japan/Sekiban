using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.Cosmos.DomainCommon.EventSourcings;
namespace Sekiban.Infrastructure.Cosmos;

public static class CosmosDbServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanCosmosDB(
        this IServiceCollection services,
        Func<SekibanCosmosOptions, SekibanCosmosOptions>? optionsFunc = null)
    {
        var options = optionsFunc is null ? new SekibanCosmosOptions() : optionsFunc(new SekibanCosmosOptions());
        services.AddSingleton(options);
        services.AddTransient<CosmosDbFactory>();
        services.AddTransient<IDocumentPersistentWriter, CosmosDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, CosmosDocumentRepository>();
        services.AddTransient<IDocumentRemover, CosmosDbDocumentRemover>();
        services.AddTransient<IBlobAccessor, AzureBlobAccessor>();
        return services;
    }
}
