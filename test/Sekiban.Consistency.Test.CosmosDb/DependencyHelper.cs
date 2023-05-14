using FeatureCheck.Domain.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Dependency;
using Sekiban.Core.Shared;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Testing.Story;
namespace SampleProjectStoryXTest;

public static class DependencyHelper
{
    public static ServiceProvider CreateDefaultProvider(
        ISekibanTestFixture fixture,
        DatabaseType databaseType = DatabaseType.CosmosDb,
        ISekibanDateProducer? sekibanDateProducer = null,
        ServiceCollectionExtensions.MultiProjectionType multiProjectionType =
            ServiceCollectionExtensions.MultiProjectionType.MemoryCache)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(fixture.Configuration);
        if (databaseType == DatabaseType.InMemory)
        {
            services.AddSekibanCoreInMemoryTestWithDependency(new FeatureCheckDependency());
        }
        else if (databaseType == DatabaseType.CosmosDb)
        {
            services.AddSekibanCoreWithDependency(new FeatureCheckDependency(), sekibanDateProducer, multiProjectionType);
            services.AddSekibanCosmosDB();
        } else if (databaseType == DatabaseType.DynamoDb)
        {
            services.AddSekibanCoreWithDependency(new FeatureCheckDependency(), sekibanDateProducer, multiProjectionType);
            services.AddSekibanDynamoDB();
        }
        if (fixture.TestOutputHelper is not null)
        {
            services.AddSingleton(fixture.TestOutputHelper);
        }

        services.AddQueriesFromDependencyDefinition(new FeatureCheckDependency());
        return services.BuildServiceProvider();
    }

    public static class LoginType
    {
        public const int Admin = 1;
        public const int Customer = 2;
    }
    
    public enum DatabaseType
    {
        InMemory,
        CosmosDb,
        DynamoDb,
    }
}
