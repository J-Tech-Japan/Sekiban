using System.Reflection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G23 binary-compatibility regression tests: every public executor constructor that existed before the
///     IExecutedUserProvider parameter was added must still be present in the public API surface.
/// </summary>
public class ExecutorBinaryCompatibilityTests
{
    [Theory]
    [InlineData(typeof(CoreGeneralSekibanExecutor), "Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.Actors.IActorObjectAccessor,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher")]
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
    [InlineData(typeof(ITagConsistentActorCommon))]
    [InlineData(typeof(GeneralTagConsistentActor))]
    [InlineData(typeof(TagReservationHelper))]
    public void MakeReservationAsync_ClrStringSignature_IsUnchanged(Type type)
    {
        var method = type.GetMethod(
            type == typeof(TagReservationHelper) ? "RequestReservationAsync" : "MakeReservationAsync",
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.Equal(typeof(string), method!.GetParameters()[^1].ParameterType);
    }
}
