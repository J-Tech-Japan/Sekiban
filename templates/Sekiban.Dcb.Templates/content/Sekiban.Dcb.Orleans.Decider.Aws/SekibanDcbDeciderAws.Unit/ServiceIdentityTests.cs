using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.ServiceId;
using SekibanDcbDeciderAws.ApiService;

namespace SekibanDcbDeciderAws.Unit;

public class ServiceIdentityTests
{
    [TestCase(null, "sekiban-app")]
    [TestCase("orders", "orders")]
    public void ConfiguredIdentity_ReplacesStorageDefaultsAndScopesMaintenanceKeys(string? configured, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Sekiban:ServiceId"] = configured, ["Orleans:ServiceId"] = "cluster-identity" }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        services.AddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        ServiceIdentity.Register(services, ServiceIdentity.Resolve(configuration, "sekiban-app"));
        Assert.That(services.Count(s => s.ServiceType == typeof(IServiceIdProvider)), Is.EqualTo(1));
        using var provider = services.BuildServiceProvider();
        var identity = provider.GetRequiredService<IServiceIdProvider>().GetCurrentServiceId();
        Assert.That(identity, Is.EqualTo(expected));
        Assert.That(ServiceIdGrainKey.Build(identity, "WeatherForecastProjection"),
            Is.EqualTo(expected + "|WeatherForecastProjection"));
    }
}
