using Dcb.Domain;
using Dcb.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using ResultBoxes;
using System.Reflection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
using OrleansExecutor = Sekiban.Dcb.Orleans.OrleansDcbExecutor;

namespace Sekiban.Dcb.Tests;

public sealed class ExecutorSizeGateCompatibilityTests
{
    [Fact]
    public void WithResultFacades_KeepLegacyConstructorsAndExposeAdditiveGateConstructors()
    {
        Assert.Contains(
            typeof(GeneralSekibanExecutor).GetConstructors(),
            constructor => constructor.GetParameters().Any(p => p.ParameterType == typeof(ExecutorSizeGateOptions)));

        var orleansType = Assembly.Load("Sekiban.Dcb.Orleans.WithResult")
            .GetType("Sekiban.Dcb.Orleans.OrleansDcbExecutor");
        Assert.NotNull(orleansType);
        Assert.Contains(
            orleansType!.GetConstructors(BindingFlags.Instance | BindingFlags.Public),
            constructor => constructor.GetParameters().Any(p => p.ParameterType == typeof(ExecutorSizeGateOptions)));
    }

    [Fact]
    public async Task OrleansDi_ResolvesTheOptInGateAndRejectsBeforeTheStore()
    {
        var domain = DomainType.GetDomainTypes();
        var store = new CoreInMemoryEventStore(domain.EventTypes);
        var services = new ServiceCollection()
            .AddSingleton(domain)
            .AddSingleton<IEventStore>(store)
            .AddSingleton<IClusterClient>(DispatchProxy.Create<IClusterClient, UnusedClusterClientProxy>())
            .AddSekibanDcbExecutorSizeGate(options => options.Add(new ExecutorSizePolicy(
                "logical-event",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 1)))
            .AddTransient<OrleansExecutor>();

        using var provider = services.BuildServiceProvider();
        var executor = provider.GetRequiredService<OrleansExecutor>();
        var result = await executor.ExecuteAsync(
            new OrleansGateCommand(Guid.NewGuid()),
            (command, _) => Task.FromResult(EventOrNone.Event(
                new WeatherForecastCreated(command.Id, "Tokyo", new DateOnly(2026, 9, 9), 21, "size gate"),
                new FallbackTag("gate", command.Id.ToString("N")))));

        Assert.IsType<ExecutorSizeLimitExceededException>(result.GetException());
        Assert.Empty((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    private sealed record OrleansGateCommand(Guid Id) : ICommand;

    private class UnusedClusterClientProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected cluster call: {targetMethod?.Name}");
    }
}
