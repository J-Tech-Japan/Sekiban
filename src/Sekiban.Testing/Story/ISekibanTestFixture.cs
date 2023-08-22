using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;
namespace Sekiban.Testing.Story;

/// <summary>
///     General Fixture for Sekiban Test
/// </summary>
public interface ISekibanTestFixture
{
    /// <summary>
    ///     Configuration
    /// </summary>
    public IConfigurationRoot Configuration { get; set; }
    /// <summary>
    ///     TestOutputHelper
    /// </summary>
    public ITestOutputHelper? TestOutputHelper { get; set; }
}
