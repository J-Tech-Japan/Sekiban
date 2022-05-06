using ESSampleProjectDependency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace SampleProjectStoryXTest;

public static class DependencyHelper
{
    public static ServiceProvider CreateDefaultProvider(TestFixture fixture, int loginType = LoginType.Admin)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(fixture.Configuration);
        Dependency.Register(services);

        return services.BuildServiceProvider();
    }
    public static class LoginType
    {
        public const int Admin = 1;
        public const int Customer = 2;
    }
}
