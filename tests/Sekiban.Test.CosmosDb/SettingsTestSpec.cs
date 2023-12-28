using FeatureCheck.Domain.Shared;
using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;
using Sekiban.Testing.Shared;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb;

public class SettingsTestSpec : TestBase<FeatureCheckDependency>
{
    private readonly IConfiguration _configuration;
    public SettingsTestSpec(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new InMemorySekibanServiceProviderGenerator()) =>
        _configuration = GetService<IConfiguration>();
    [Fact]
    public void TestSettings()
    {
        var settings = SekibanSettings.FromConfigurationSection(configuration.GetSection("Sekiban"));
        Assert.NotNull(settings);
    }
}
