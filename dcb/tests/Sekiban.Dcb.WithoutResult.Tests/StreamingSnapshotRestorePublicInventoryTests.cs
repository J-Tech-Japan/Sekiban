using System.Reflection;
using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.WithoutResult.Tests;

/// <summary>
///     Pins the exception-based public registry implementations separately from the WithResult facade. This prevents
///     an additive streaming-capability change from being accidentally present only on one command-result surface.
/// </summary>
public sealed class StreamingSnapshotRestorePublicInventoryTests
{
    [Fact]
    public void WithoutResult_streaming_registry_implementations_keep_the_complete_public_capability_shape()
    {
        AssertStreamingRegistrySurface(typeof(SimpleMultiProjectorTypes));
        AssertStreamingRegistrySurface(typeof(AotWithoutResultMultiProjectorTypes));
    }

    private static void AssertStreamingRegistrySurface(Type implementation)
    {
        Assert.True(implementation.IsPublic);
        Assert.Contains(typeof(IStreamingMultiProjectorTypes), implementation.GetInterfaces());

        var supports = Assert.Single(
            implementation.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(IStreamingMultiProjectorTypes.SupportsStreamDeserialization));
        Assert.Equal(typeof(bool), supports.ReturnType);
        Assert.Equal([typeof(string)], supports.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(["projectorName"], supports.GetParameters().Select(parameter => parameter.Name));

        var deserialize = Assert.Single(
            implementation.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(IStreamingMultiProjectorTypes.DeserializeFromStreamAsync));
        Assert.Equal(typeof(Task<ResultBox<IMultiProjectionPayload>>), deserialize.ReturnType);
        Assert.Equal(
            [typeof(string), typeof(DcbDomainTypes), typeof(string), typeof(Stream), typeof(CancellationToken)],
            deserialize.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            ["projectorName", "domainTypes", "safeWindowThreshold", "source", "cancellationToken"],
            deserialize.GetParameters().Select(parameter => parameter.Name));
        Assert.True(deserialize.GetParameters()[4].IsOptional);
    }
}
