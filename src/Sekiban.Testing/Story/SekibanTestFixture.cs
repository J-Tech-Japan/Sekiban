using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.PlatformAbstractions;
using System.Reflection;
namespace Sekiban.Testing.Story;

public interface ISekibanTestFixture
{
    public IConfigurationRoot Configuration { get; set; }
}
