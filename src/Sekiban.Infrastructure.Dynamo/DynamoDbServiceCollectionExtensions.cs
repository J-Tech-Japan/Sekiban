using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.Dynamo.Documents;
namespace Sekiban.Infrastructure.Dynamo;

public static class DynamoDbServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanDynamoDB(this IServiceCollection services)
    {
        // データストア
        services.AddTransient<DynamoDbFactory>();

        services.AddTransient<IDocumentPersistentWriter, DynamoDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, DynamoDocumentRepository>();
        services.AddTransient<IDocumentRemover, DynamoDbDocumentRemover>();
        return services;
    }    
}
