using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.PlatformAbstractions;
namespace Sekiban.EventSourcing.TestHelpers.StoryTests
{
    public class SekibanTestFixture
    {
        public IConfigurationRoot Configuration { get; set; }
        public SekibanTestFixture()
        {
            var builder = new ConfigurationBuilder().SetBasePath(PlatformServices.Default.Application.ApplicationBasePath)
                .AddJsonFile("appsettings.json", false, false)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }
    }
}
