using ESSampleProjectDependency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing;
using Sekiban.EventSourcing.TestHelpers;
namespace SampleProjectStoryXTest
{
    public static class DependencyHelper
    {
        public static ServiceProvider CreateDefaultProvider(
            TestFixture fixture,
            bool inMemory = false,
            ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(fixture.Configuration);
            if (inMemory)
            {
                Dependency.RegisterForInMemoryTest(services);
            } else
            {
                Dependency.Register(services, multipleProjectionType);
            }
            return services.BuildServiceProvider();
        }
        public static class LoginType
        {
            public const int Admin = 1;
            public const int Customer = 2;
        }
    }
}
