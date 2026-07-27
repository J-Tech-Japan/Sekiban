using System.Reflection;
using Sekiban.Dcb.Orleans.Grains;
using Xunit;
namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
///     SEK-G18 public-API baseline guard: the durable "rebuild required" marker is an Orleans-persisted implementation
///     detail and MUST NOT appear on any public API surface (packet: no public API additions / signature changes). The
///     marker is reachable only through the INTERNAL <c>IRebuildMarkerState</c> seam and an INTERNAL Orleans-serialized
///     field. This test fails if a future change re-exposes it (e.g. re-adds it to the public read interface, or makes the
///     field/seam public).
/// </summary>
public class RebuildMarkerPublicApiBaselineTests
{
    [Fact]
    public void RebuildMarker_IsAbsentFromThePublicReadInterface()
    {
        var readInterface = typeof(IReadOnlyMultiProjectionGrainState);
        Assert.True(readInterface.IsPublic);
        Assert.Null(readInterface.GetProperty("RebuildRequired"));
        Assert.DoesNotContain(readInterface.GetMembers(), m => m.Name == "RebuildRequired");
    }

    [Fact]
    public void RebuildMarker_IsNotAPublicMemberOfTheGrainState_ButStillPersistedInternally()
    {
        var stateType = typeof(MultiProjectionGrainState);
        Assert.True(stateType.IsPublic);

        // Not a PUBLIC member (would be a public API addition)...
        Assert.Null(stateType.GetProperty("RebuildRequired", BindingFlags.Public | BindingFlags.Instance));

        // ...but it still exists as a NON-PUBLIC instance member so the durable marker is persisted/restored.
        Assert.NotNull(stateType.GetProperty("RebuildRequired", BindingFlags.NonPublic | BindingFlags.Instance));
    }

    [Fact]
    public void RebuildMarkerSeam_Interface_IsInternal()
    {
        var seam = typeof(MultiProjectionGrainState).Assembly
            .GetType("Sekiban.Dcb.Orleans.Grains.IRebuildMarkerState");
        Assert.NotNull(seam);                         // the seam exists (reachable only by name / within the assembly)
        Assert.False(seam!.IsPublic, "IRebuildMarkerState must remain internal — it is not part of the public API");
    }
}
