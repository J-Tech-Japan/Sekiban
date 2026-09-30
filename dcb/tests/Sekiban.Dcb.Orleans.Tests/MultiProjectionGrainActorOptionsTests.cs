using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Orleans.Grains;
using System.Reflection;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class MultiProjectionGrainActorOptionsTests
{
    [Fact]
    public void MergeActorOptions_PreservesEveryPublicSettableOptionExceptPersistPolicyOverlay()
    {
        var baseOptions = new GeneralMultiProjectionActorOptions();
        var properties = typeof(GeneralMultiProjectionActorOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.SetMethod is { IsPublic: true })
            .ToArray();
        var overlayNames = new[]
        {
            nameof(GeneralMultiProjectionActorOptions.PersistBatchSize),
            nameof(GeneralMultiProjectionActorOptions.PersistIntervalSeconds),
            nameof(GeneralMultiProjectionActorOptions.SkipPersistWhenSafeCheckpointUnchanged)
        };
        foreach (var name in overlayNames)
        {
            Assert.Contains(properties, property => property.Name == name);
        }

        foreach (var property in properties)
        {
            var initialValue = property.GetValue(baseOptions);
            var value = CreateDifferentValue(property, initialValue);
            Assert.NotEqual(initialValue, value);
            property.SetValue(baseOptions, value);
        }

        var policy = new MultiProjectionGrain.PersistPolicySettings(
            baseOptions.PersistBatchSize + 17,
            TimeSpan.FromSeconds(baseOptions.PersistIntervalSeconds + 23.75),
            !baseOptions.SkipPersistWhenSafeCheckpointUnchanged);
        var merged = MultiProjectionGrain.MergeActorOptions(baseOptions, policy);

        Assert.NotSame(baseOptions, merged);
        foreach (var property in properties.Where(property => !overlayNames.Contains(property.Name)))
        {
            Assert.True(
                Equals(property.GetValue(baseOptions), property.GetValue(merged)),
                $"Option {property.Name} was not propagated.");
        }

        Assert.Same(baseOptions.ProjectorPersistenceOverrides, merged.ProjectorPersistenceOverrides);
        Assert.Equal(policy.PersistBatchSize, merged.PersistBatchSize);
        Assert.Equal((int)policy.PersistInterval.TotalSeconds, merged.PersistIntervalSeconds);
        Assert.Equal(policy.SkipPersistWhenSafeCheckpointUnchanged, merged.SkipPersistWhenSafeCheckpointUnchanged);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(12.75, 12)]
    public void MergeActorOptions_OverlaysPersistPolicy(double intervalSeconds, int expectedIntervalSeconds)
    {
        var baseOptions = new GeneralMultiProjectionActorOptions
        {
            PersistBatchSize = 123,
            PersistIntervalSeconds = 456,
            SkipPersistWhenSafeCheckpointUnchanged = true
        };
        var policy = new MultiProjectionGrain.PersistPolicySettings(789, TimeSpan.FromSeconds(intervalSeconds), false);

        var merged = MultiProjectionGrain.MergeActorOptions(baseOptions, policy);

        Assert.Equal(789, merged.PersistBatchSize);
        Assert.Equal(expectedIntervalSeconds, merged.PersistIntervalSeconds);
        Assert.False(merged.SkipPersistWhenSafeCheckpointUnchanged);
        Assert.Equal(123, baseOptions.PersistBatchSize);
        Assert.Equal(456, baseOptions.PersistIntervalSeconds);
        Assert.True(baseOptions.SkipPersistWhenSafeCheckpointUnchanged);
    }

    [Fact]
    public void MergeActorOptions_HostCreationOptionsEnableVerifySafeStateIsolation()
    {
        var baseOptions = new GeneralMultiProjectionActorOptions { VerifySafeStateIsolation = true };
        var policy = new MultiProjectionGrain.PersistPolicySettings(100, TimeSpan.FromSeconds(30), true);

        var hostOptions = MultiProjectionGrain.MergeActorOptions(baseOptions, policy);

        Assert.True(hostOptions.VerifySafeStateIsolation);
    }

    private static object CreateDifferentValue(PropertyInfo property, object? initialValue) => initialValue switch
    {
        bool value => !value,
        int value => value + 1,
        long value => value + 1,
        double value => value + 0.125,
        Dictionary<string, MultiProjectionPersistenceOverrideOptions> =>
            new Dictionary<string, MultiProjectionPersistenceOverrideOptions>(StringComparer.Ordinal)
            {
                ["TestProjection"] = new() { PersistBatchSize = 37 }
            },
        _ => throw new InvalidOperationException(
            $"Add a non-default value generator for option {property.Name} ({property.PropertyType}).")
    };
}
