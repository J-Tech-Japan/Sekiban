using System.Reflection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.WithoutResult.Tests;

/// <summary>
///     SEK-G23 binary-compatibility regression tests: every public executor constructor that existed before the
///     IExecutedUserProvider parameter was added must still be present in the public API surface.
/// </summary>
public class ExecutorBinaryCompatibilityTests
{
    [Theory]
    [InlineData(typeof(GeneralSekibanExecutor), "Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.Actors.IActorObjectAccessor,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher")]
    [InlineData(typeof(InMemoryDcbExecutor), "Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Storage.IEventStore")]
    [InlineData(typeof(InMemoryDcbExecutorForTesting), "Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Storage.IEventStore")]
    public void PreSekibanG23_Constructor_Overload_Is_Public(Type executorType, string expectedParameterTypeNames)
    {
        var expected = expectedParameterTypeNames.Split(',');
        var constructors = executorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        var matching = constructors.FirstOrDefault(c =>
            c.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(expected));

        Assert.NotNull(matching);
        Assert.True(matching.IsPublic);
    }

    [Theory]
    [InlineData(typeof(GeneralSekibanExecutor), "Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.Actors.IActorObjectAccessor,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher,Sekiban.Dcb.IExecutedUserProvider")]
    [InlineData(typeof(InMemoryDcbExecutor), "Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.IExecutedUserProvider")]
    [InlineData(typeof(InMemoryDcbExecutorForTesting), "Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.IExecutedUserProvider")]
    public void PreSekibanG31_LongestConstructor_IsStillPublic(Type executorType, string expectedParameterTypeNames)
    {
        var expected = expectedParameterTypeNames.Split(',');
        Assert.Contains(
            executorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public),
            constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(expected));
    }

    [Fact]
    public void PreSekibanG23_Orleans_WithoutResult_Constructor_Overload_Is_Public()
    {
        var assembly = Assembly.Load("Sekiban.Dcb.Orleans.WithoutResult");
        var executorType = assembly.GetType("Sekiban.Dcb.Orleans.OrleansDcbExecutor");
        Assert.NotNull(executorType);

        var expected = "Orleans.IClusterClient,Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher,Sekiban.Dcb.ServiceId.IServiceIdProvider".Split(',');

        var constructors = executorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        var matching = constructors.FirstOrDefault(c =>
            c.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(expected));

        Assert.NotNull(matching);
        Assert.True(matching.IsPublic);
    }

    [Fact]
    public void PreSekibanG31_LongestOrleansWithoutResultConstructor_IsStillPublic()
    {
        var assembly = Assembly.Load("Sekiban.Dcb.Orleans.WithoutResult");
        var executorType = assembly.GetType("Sekiban.Dcb.Orleans.OrleansDcbExecutor");
        Assert.NotNull(executorType);
        var expected = "Orleans.IClusterClient,Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher,Sekiban.Dcb.ServiceId.IServiceIdProvider,Sekiban.Dcb.IExecutedUserProvider".Split(',');
        Assert.Contains(
            executorType!.GetConstructors(BindingFlags.Instance | BindingFlags.Public),
            constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType.FullName)
                .SequenceEqual(expected));
    }
}
