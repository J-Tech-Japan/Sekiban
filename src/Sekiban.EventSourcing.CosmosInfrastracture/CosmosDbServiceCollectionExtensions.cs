using CosmosInfrastructure.DomainCommon.EventSourcings;
using Microsoft.Extensions.DependencyInjection;
namespace CosmosInfrastructure;

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
