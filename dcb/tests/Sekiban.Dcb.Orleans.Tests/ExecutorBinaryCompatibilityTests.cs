using System.Reflection;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Storage;
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

    [Fact]
    public void PreSekibanG24_MultiProjectionGrain_Constructor_Is_Still_Public()
    {
        // The registry dependencies were added through an overload. Keep the exact pre-G24 surface available to
        // already-compiled Orleans activation factories and direct consumers.
        var legacy = typeof(MultiProjectionGrain)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length == 11 &&
                    parameters[^1].ParameterType == typeof(Sekiban.Dcb.ServiceId.IServiceIdProvider) &&
                    parameters.All(parameter => parameter.ParameterType != typeof(IProjectionStatusStore));
            });

        Assert.NotNull(legacy);
        Assert.True(legacy!.IsPublic);
    }
}
