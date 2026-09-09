using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.Weather;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;

namespace Sekiban.Dcb.WithoutResult.Tests;

public sealed class ExecutorSizeGateCompatibilityTests
{
    [Fact]
    public void WithoutResultFacades_KeepLegacyConstructorsAndExposeAdditiveGateConstructors()
    {
        Assert.Contains(
            typeof(GeneralSekibanExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Any(p => p.ParameterType == typeof(ExecutorSizeGateOptions)));

        var orleansType = Assembly.Load("Sekiban.Dcb.Orleans.WithoutResult")
            .GetType("Sekiban.Dcb.Orleans.OrleansDcbExecutor");
        Assert.NotNull(orleansType);
        Assert.Contains(
            orleansType!.GetConstructors(BindingFlags.Instance | BindingFlags.Public),
            constructor => constructor.GetParameters().Any(p => p.ParameterType == typeof(ExecutorSizeGateOptions)));
    }

    [Fact]
    public async Task WithoutResultDi_LeavesLegacyUngatedAndInjectsOptInOptions()
    {
        var domain = DomainType.GetDomainTypes();
        var legacyStore = new InMemoryEventStore(domain.EventTypes);
        var legacyServices = new ServiceCollection()
            .AddSingleton(domain)
            .AddSingleton<IEventStore>(legacyStore)
            .AddSingleton<IActorObjectAccessor>(new InMemoryObjectAccessor(legacyStore, domain))
            .AddTransient<GeneralSekibanExecutor>();

        using (var legacyProvider = legacyServices.BuildServiceProvider())
        {
            var result = await legacyProvider.GetRequiredService<GeneralSekibanExecutor>().ExecuteAsync(
                new CreateWeatherForecast
                {
                    ForecastId = Guid.NewGuid(),
                    Location = "Tokyo",
                    Date = new DateOnly(2026, 9, 9),
                    TemperatureC = 21
                });
            Assert.Single(result.Events);
        }

        var optInStore = new InMemoryEventStore(domain.EventTypes);
        var optInServices = new ServiceCollection()
            .AddSingleton(domain)
            .AddSingleton<IEventStore>(optInStore)
            .AddSingleton<IActorObjectAccessor>(new InMemoryObjectAccessor(optInStore, domain))
            .AddSekibanDcbExecutorSizeGate(options => options.Add(new ExecutorSizePolicy(
                "logical-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 1)))
            .AddTransient<GeneralSekibanExecutor>();

        using var optInProvider = optInServices.BuildServiceProvider();
        await Assert.ThrowsAsync<ExecutorSizeLimitExceededException>(() =>
            optInProvider.GetRequiredService<GeneralSekibanExecutor>().ExecuteAsync(
                new CreateWeatherForecast
                {
                    ForecastId = Guid.NewGuid(),
                    Location = "Tokyo",
                    Date = new DateOnly(2026, 9, 9),
                    TemperatureC = 21
                }));
        Assert.Empty((await optInStore.ReadAllSerializableEventsAsync()).GetValue());
    }
}
