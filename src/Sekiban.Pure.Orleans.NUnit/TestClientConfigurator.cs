using Microsoft.Extensions.Configuration;
using Orleans.TestingHost;
namespace Sekiban.Pure.Orleans.NUnit;

public class TestClientConfigurator : IClientBuilderConfigurator
{
    public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
    {
        clientBuilder.ConfigureServices(
            services =>
            {
                // services.AddSekibanOrleansClient();
            });
    }
}