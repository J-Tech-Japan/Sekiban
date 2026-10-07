using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans.Streams;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.ServiceId;
using SekibanDcbOrleansAws.ApiService;

namespace SekibanDcbOrleansAws.Unit;

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
    [TestCase("sekiban-app")]
    [TestCase("another-app")]
    public void PublisherAndProjectionSubscription_UseTheSameScopedStream(string serviceId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStreamDestinationResolver>(sp =>
            new DefaultOrleansStreamDestinationResolver("EventStreamProvider", "AllEvents", Guid.Empty,
                sp.GetRequiredService<IServiceIdProvider>()));
        services.AddSingleton<IEventSubscriptionResolver>(new DefaultOrleansEventSubscriptionResolver());
        ServiceIdentity.Register(services, serviceId);
        using var provider = services.BuildServiceProvider();
        var destination = (OrleansSekibanStream)provider.GetRequiredService<IStreamDestinationResolver>()
            .Resolve(null!, []).Single();
        var subscription = (OrleansSekibanStream)provider.GetRequiredService<IEventSubscriptionResolver>()
            .Resolve(ServiceIdGrainKey.Build(serviceId, "ReservationProjector"));
        Assert.That(destination.StreamNamespace, Is.EqualTo(serviceId + "|AllEvents"));
        Assert.That(subscription.StreamNamespace, Is.EqualTo(destination.StreamNamespace));
        Assert.That(subscription.ProviderName, Is.EqualTo(destination.ProviderName));
        Assert.That(subscription.StreamId, Is.EqualTo(destination.StreamId));
    }
}
