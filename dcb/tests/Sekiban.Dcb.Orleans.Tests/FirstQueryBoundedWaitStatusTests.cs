using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Sekiban.Dcb.Orleans.Grains;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class FirstQueryBoundedWaitStatusTests
{
    [Fact]
    public void Status_RealOrleansSerializer_RoundTripsAppendedNonDefaultFields()
    {
        using var services = new ServiceCollection().AddSerializer().BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer<MultiProjectionGrainStatus>>();
        var status = new MultiProjectionGrainStatus("test", false, false, "current", 7,
            null, null, 1, 1, 0, false, null, false, true, 2,
            FirstQueryCatchUpPending: true, CatchUpTargetPosition: "target",
            LastBackgroundCatchUpError: "2026-09-30T00:00:00Z: store unavailable");
        var restored = serializer.Deserialize(serializer.SerializeToArray(status));
        Assert.NotNull(restored);
        Assert.Equal(status, restored);
        Assert.True(restored.FirstQueryCatchUpPending);
        Assert.Equal("target", restored.CatchUpTargetPosition);
        Assert.Equal(status.LastBackgroundCatchUpError, restored.LastBackgroundCatchUpError);
    }
}
