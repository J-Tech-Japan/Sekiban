using ESSampleProjectDependency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace SampleProjectStoryXTest;

public static class DependencyHelper
{
    public static ServiceProvider CreateDefaultProvider(TestFixture fixture, bool inMemory = false, int loginType = LoginType.Admin)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(fixture.Configuration);
        if (inMemory)
        {
            Dependency.RegisterForInMemoryTest(services);
        } else
        {
            Dependency.Register(services);
        }
        return services.BuildServiceProvider();
    }
    public static class LoginType
    {
        public const int Admin = 1;
        public const int Customer = 2;
    }
}
