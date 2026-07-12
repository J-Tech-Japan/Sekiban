using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Tags;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     The structured failure contract: a partially-failed write must say which events are already visible
///     and which never landed, so a caller can act without guessing — and it must be clear that nothing was
///     deleted.
/// </summary>
public class CosmosPartialEventWriteExceptionTests
{
    [Fact]
    public void Should_Name_The_Visible_And_The_Failed_Event_Sets()
    {
        var written = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var failed = new[] { Guid.NewGuid() };

        var exception = new CosmosPartialEventWriteException(
            written,
            failed,
            new InvalidOperationException("create failed"));

        Assert.Equal(written, exception.WrittenEventIds);
        Assert.Equal(failed, exception.FailedEventIds);
        Assert.Contains(written[0].ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(failed[0].ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("NOT deleted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Tag_Write_Exhaustion_Should_Name_The_Events_Left_Without_Complete_Tags()
    {
        var eventIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var exception = new CosmosTagWriteExhaustedException(
            eventIds,
            4,
            new InvalidOperationException("tag write failed"));

        Assert.Equal(eventIds, exception.EventIds);
        Assert.Equal(4, exception.Attempts);
        Assert.Contains("NOT deleted", exception.Message, StringComparison.Ordinal);
        Assert.Contains(eventIds[0].ToString(), exception.Message, StringComparison.Ordinal);
    }
}
