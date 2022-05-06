using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.PlatformAbstractions;
namespace SampleProjectStoryXTest;

public class TestFixture
{
    public IConfigurationRoot Configuration { get; set; }
    public TestFixture()
    {
        var builder = new ConfigurationBuilder().SetBasePath(PlatformServices.Default.Application.ApplicationBasePath)
            .AddJsonFile("appsettings.json", false, false)
            .AddEnvironmentVariables();
        Configuration = builder.Build();
    }
}
