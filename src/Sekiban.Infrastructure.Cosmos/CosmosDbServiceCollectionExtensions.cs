using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.Cosmos.DomainCommon.EventSourcings;
namespace Sekiban.Infrastructure.Cosmos;

public static class CosmosDbServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanCosmosDB(this IServiceCollection services)
    {
        // データストア
        services.AddTransient<CosmosDbFactory>();

        services.AddTransient<IDocumentPersistentWriter, CosmosDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, CosmosDocumentRepository>();

        return services;
    }
}
