using System.Reflection;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G23 binary-compatibility regression tests: the public WithResult Orleans executor constructor that existed
///     before the IExecutedUserProvider parameter was added must still be present in the public API surface.
/// </summary>
public class ExecutorBinaryCompatibilityTests
{
    [Fact]
    public void PreSekibanG23_Constructor_Overload_Is_Public()
    {
        var executorType = typeof(OrleansDcbExecutor);
        var expected = "Orleans.IClusterClient,Sekiban.Dcb.Storage.IEventStore,Sekiban.Dcb.DcbDomainTypes,Sekiban.Dcb.Actors.IEventPublisher,Sekiban.Dcb.ServiceId.IServiceIdProvider".Split(',');

        var constructors = executorType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        var matching = constructors.FirstOrDefault(c =>
            c.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(expected));

        Assert.NotNull(matching);
        Assert.True(matching.IsPublic);
    }
}
