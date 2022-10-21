using Microsoft.Extensions.Configuration;
namespace Sekiban.Testing.Story;

public interface ISekibanTestFixture
{
    public IConfigurationRoot Configuration { get; set; }
}
