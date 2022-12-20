using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace Sekiban.Testing.Story;

public interface ISekibanTestFixture
{
    public IConfigurationRoot Configuration { get; set; }
    public ITestOutputHelper TestOutputHelper { get; set; }
}
