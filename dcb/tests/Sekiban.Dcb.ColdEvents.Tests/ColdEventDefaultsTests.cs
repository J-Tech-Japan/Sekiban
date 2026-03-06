using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.ColdEvents.Tests;

public class ColdEventDefaultsTests
{
    [Fact]
    public void AddSekibanDcbColdEventDefaults_should_register_runner_dependencies()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbColdEventDefaults();

        Assert.Contains(services, d => d.ServiceType == typeof(ColdExportCycleRunner));
        Assert.Contains(services, d => d.ServiceType == typeof(IOptions<ColdEventStoreOptions>));
        Assert.Contains(services, d => d.ServiceType == typeof(IServiceIdProvider));
    }
}
