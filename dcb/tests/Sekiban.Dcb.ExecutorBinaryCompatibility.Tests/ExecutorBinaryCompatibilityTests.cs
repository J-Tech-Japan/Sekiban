using System.Reflection;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G23 binary-compatibility regression tests: every public executor constructor that existed before the
///     IExecutedUserProvider parameter was added must still be present in the public API surface. All assertions are
///     in this single test project so the same test skeleton is not duplicated across test assemblies.
/// </summary>
public class ExecutorBinaryCompatibilityTests
{
    public static TheoryData<string, string, string> PreSekibanG23ConstructorCases => new()
    {
        {
            "Sekiban.Dcb.Core",
            "Sekiban.Dcb.Actors.CoreGeneralSekibanExecutor",
            "Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.Actors.IActorObjectAccessor,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher"
        },
        {
            "Sekiban.Dcb.WithResult",
            "Sekiban.Dcb.Actors.GeneralSekibanExecutor",
            "Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.Actors.IActorObjectAccessor,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher"
        },
        {
            "Sekiban.Dcb.WithResult",
            "Sekiban.Dcb.InMemory.InMemoryDcbExecutor",
            "Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Storage.IEventStore"
        },
        {
            "Sekiban.Dcb.WithResult.Testing",
            "Sekiban.Dcb.Testing.InMemoryDcbExecutorForTesting",
            "Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Storage.IEventStore"
        },
        {
            "Sekiban.Dcb.WithoutResult",
            "Sekiban.Dcb.Actors.GeneralSekibanExecutor",
            "Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.Actors.IActorObjectAccessor,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher"
        },
        {
            "Sekiban.Dcb.WithoutResult",
            "Sekiban.Dcb.InMemory.InMemoryDcbExecutor",
            "Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Storage.IEventStore"
        },
        {
            "Sekiban.Dcb.WithoutResult.Testing",
            "Sekiban.Dcb.Testing.InMemoryDcbExecutorForTesting",
            "Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Storage.IEventStore"
        },
        {
            "Sekiban.Dcb.Orleans.WithResult",
            "Sekiban.Dcb.Orleans.OrleansDcbExecutor",
            "Orleans.IClusterClient,Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher,Sekiban.Dcb.ServiceId.IServiceIdProvider"
        },
        {
            "Sekiban.Dcb.Orleans.WithoutResult",
            "Sekiban.Dcb.Orleans.OrleansDcbExecutor",
            "Orleans.IClusterClient,Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher,Sekiban.Dcb.ServiceId.IServiceIdProvider"
        }
    };

    [Theory]
    [MemberData(nameof(PreSekibanG23ConstructorCases))]
    public void PreSekibanG23_Constructor_Overload_Is_Public(string assemblyName, string typeName, string expectedParameterTypeNames)
    {
        var executorType = Assembly.Load(assemblyName).GetType(typeName);
        Assert.NotNull(executorType);

        var expected = expectedParameterTypeNames.Split(',');
        var constructors = executorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        var matching = constructors.FirstOrDefault(c =>
            c.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(expected));

        Assert.NotNull(matching);
        Assert.True(matching.IsPublic);
    }
}
