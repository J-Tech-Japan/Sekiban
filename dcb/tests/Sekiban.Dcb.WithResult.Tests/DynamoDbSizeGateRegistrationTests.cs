using System.Reflection;
using Dcb.Domain.Student;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.SizeGates;

namespace Sekiban.Dcb.Tests;

public sealed class DynamoDbSizeGateRegistrationTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public void MixedCoreAndProviderRegistrationIsRejectedInEveryOrderWithoutMutation(
        bool coreUsesInstance,
        bool providerFirst,
        int providerHelper)
    {
        var services = new ServiceCollection();
        var callbackInvoked = false;

        if (providerFirst)
        {
            RegisterProvider(services, providerHelper);
            var before = services.ToArray();

            var exception = Assert.Throws<InvalidOperationException>(() =>
                RegisterCore(services, coreUsesInstance, () => callbackInvoked = true));

            Assert.False(callbackInvoked);
            Assert.Equal(before, services.ToArray());
            AssertConflictMessage(exception);
            return;
        }

        RegisterCore(services, coreUsesInstance, () => callbackInvoked = true);
        Assert.True(coreUsesInstance || callbackInvoked);
        var coreBeforeProvider = services.ToArray();

        var providerException = Assert.Throws<InvalidOperationException>(() =>
            RegisterProvider(services, providerHelper));

        Assert.Equal(coreBeforeProvider, services.ToArray());
        AssertConflictMessage(providerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidCoreRegistrationLeavesCollectionUntouchedAndProviderCanRegister(bool useInstance)
    {
        var services = new ServiceCollection();
        var before = services.ToArray();
        var invalid = new ExecutorSizeGateOptions()
            .Add(new ExecutorSizePolicy(
                "invalid",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 0));

        Assert.Throws<ArgumentException>(() =>
            RegisterInvalidCore(services, useInstance, invalid));

        Assert.Equal(before, services.ToArray());
        RegisterProvider(services, 0);

        using var provider = services.BuildServiceProvider();
        Assert.Single(provider.GetRequiredService<ExecutorSizeGateOptions>().Policies);
    }

    [Fact]
    public void CoreRegistrationIsLastWinsAndPreservesSuppliedInstanceIdentity()
    {
        var first = ValidCoreOptions("first");
        var second = ValidCoreOptions("second");
        var services = new ServiceCollection()
            .AddSekibanDcbExecutorSizeGate(first)
            .AddSekibanDcbExecutorSizeGate(second);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<ExecutorSizeGateOptions>();

        Assert.Same(second, resolved);
        Assert.Equal("second", Assert.Single(resolved.Policies).Scope);
        Assert.Equal(
            2,
            services.Count(descriptor => descriptor.ServiceType == typeof(ExecutorSizeGateOptions)));
    }

    [Fact]
    public void ProviderHelpersAccumulateAndPreserveTheirExistingPolicyOrder()
    {
        using var provider = new ServiceCollection()
            .AddSekibanDcbDynamoDbEventItemSizeGate()
            .AddSekibanDcbDynamoDbMaxWrittenItemSizeGate()
            .AddSekibanDcbDynamoDbWriteOperationSizeGate()
            .BuildServiceProvider();

        var gate = provider.GetRequiredService<ExecutorSizeGateOptions>();
        Assert.Equal(
            [
                DynamoDbEventItemSizeMeasurement.Scope,
                DynamoDbMaxWrittenItemSizeMeasurement.Scope,
                DynamoDbWriteOperationSizeMeasurement.Scope
            ],
            gate.Policies.Select(policy => policy.Scope));
    }

    [Fact]
    public void NoGateRegistrationRemainsUngatedAndAddsNoInternalMarker()
    {
        var services = new ServiceCollection();

        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(ExecutorSizeGateOptions));
        Assert.DoesNotContain(
            typeof(SekibanDcbExecutorSizeGateExtensions).Assembly.GetTypes(),
            type => type.Name == "ExecutorSizeGateRegistrationMarker" && type.IsPublic);
    }

    [Fact]
    public void CoreAndProviderRegistrationUseTheSameShardOptionsForMigrationParity()
    {
        var storeOptions = new DynamoDbEventStoreOptions { WriteShardCount = 2 };
        var context = CreateContext();

        using var coreProvider = new ServiceCollection()
            .AddSekibanDcbExecutorSizeGate(options => options.AddDynamoDbEventItemPolicy(storeOptions))
            .BuildServiceProvider();
        using var providerProvider = new ServiceCollection()
            .AddSingleton<IOptions<DynamoDbEventStoreOptions>>(Options.Create(storeOptions))
            .AddSekibanDcbDynamoDbEventItemSizeGate()
            .BuildServiceProvider();

        var coreMeasurement = Assert.IsType<DynamoDbEventItemSizeMeasurement>(
            Assert.Single(coreProvider.GetRequiredService<ExecutorSizeGateOptions>().Policies).Measurement);
        var providerMeasurement = Assert.IsType<DynamoDbEventItemSizeMeasurement>(
            Assert.Single(providerProvider.GetRequiredService<ExecutorSizeGateOptions>().Policies).Measurement);
        var omittedOptionsMeasurement = new DynamoDbEventItemSizeMeasurement();

        Assert.Equal(
            coreMeasurement.Measure(context).Bytes,
            providerMeasurement.Measure(context).Bytes);
        Assert.Equal(
            coreMeasurement.Measure(context).Bytes!.Value - 2,
            omittedOptionsMeasurement.Measure(context).Bytes);
    }

    [Fact]
    public void CorePublicAbiRemainsTwoOverloadsAndMarkerIsNotPublic()
    {
        var methods = typeof(SekibanDcbExecutorSizeGateExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(SekibanDcbExecutorSizeGateExtensions.AddSekibanDcbExecutorSizeGate))
            .ToArray();

        Assert.Equal(2, methods.Length);
        Assert.Contains(methods, method => method.GetParameters().Select(p => p.ParameterType)
            .SequenceEqual([typeof(IServiceCollection), typeof(Action<ExecutorSizeGateOptions>)]));
        Assert.Contains(methods, method => method.GetParameters().Select(p => p.ParameterType)
            .SequenceEqual([typeof(IServiceCollection), typeof(ExecutorSizeGateOptions)]));
        Assert.DoesNotContain(
            typeof(SekibanDcbExecutorSizeGateExtensions).Assembly.GetExportedTypes(),
            type => type.Name == "ExecutorSizeGateRegistrationMarker");
    }

    private static void RegisterCore(
        IServiceCollection services,
        bool useInstance,
        Action callbackInvoked)
    {
        if (useInstance)
        {
            services.AddSekibanDcbExecutorSizeGate(ValidCoreOptions("core"));
            return;
        }

        services.AddSekibanDcbExecutorSizeGate(options =>
        {
            callbackInvoked();
            options.Add(new ExecutorSizePolicy(
                "core",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 1024));
        });
    }

    private static void RegisterInvalidCore(
        IServiceCollection services,
        bool useInstance,
        ExecutorSizeGateOptions invalid)
    {
        if (useInstance)
        {
            services.AddSekibanDcbExecutorSizeGate(invalid);
            return;
        }

        services.AddSekibanDcbExecutorSizeGate(options =>
            options.Add(new ExecutorSizePolicy(
                "invalid",
                ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
                maxBytesPerEvent: 0)));
    }

    private static void RegisterProvider(IServiceCollection services, int providerHelper)
    {
        switch (providerHelper)
        {
            case 0:
                services.AddSekibanDcbDynamoDbEventItemSizeGate();
                break;
            case 1:
                services.AddSekibanDcbDynamoDbMaxWrittenItemSizeGate();
                break;
            case 2:
                services.AddSekibanDcbDynamoDbWriteOperationSizeGate();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(providerHelper));
        }
    }

    private static ExecutorSizeGateOptions ValidCoreOptions(string scope) =>
        new ExecutorSizeGateOptions().Add(new ExecutorSizePolicy(
            scope,
            ExecutorSizeRepresentation.LogicalSerializedEventUtf8,
            maxBytesPerEvent: 1024));

    private static void AssertConflictMessage(InvalidOperationException exception)
    {
        Assert.Contains("cannot both configure the executor size gate", exception.Message);
        Assert.Contains("Configure every policy through one mechanism", exception.Message);
        Assert.Contains("AddSekibanDcbExecutorSizeGate", exception.Message);
        Assert.Contains("AddSekibanDcbDynamoDb", exception.Message);
    }

    private static ExecutorSizeMeasurementContext CreateContext()
    {
        var serialized = new SerializableEvent(
            [],
            SortableUniqueId.GenerateNew(),
            Guid.CreateVersion7(),
            new EventMetadata("cause", "correlation", "user"),
            [],
            nameof(StudentCreated));
        return new ExecutorSizeMeasurementContext(
            DynamoDbEventItemSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            new Event(
                new StudentCreated(Guid.CreateVersion7(), "migration", 1),
                serialized.SortableUniqueIdValue,
                serialized.EventPayloadName,
                serialized.Id,
                serialized.EventMetadata,
                serialized.Tags),
            serialized,
            "g68-registration",
            null,
            null);
    }
}
