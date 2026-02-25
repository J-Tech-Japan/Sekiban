using Sekiban.Dcb.ColdEvents;
namespace Sekiban.Dcb.ColdEvents.Tests;

public class NotSupportedColdEventStoreTests
{
    private readonly NotSupportedColdEventStore _sut = new();

    [Fact]
    public async Task GetStatusAsync_should_return_not_supported_and_not_enabled()
    {
        var status = await _sut.GetStatusAsync(CancellationToken.None);

        Assert.False(status.IsSupported);
        Assert.False(status.IsEnabled);
        Assert.Equal("Cold event store is not configured", status.Reason);
    }

    [Fact]
    public async Task GetProgressAsync_should_return_error()
    {
        var result = await _sut.GetProgressAsync("test-service", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.IsType<NotSupportedException>(result.GetException());
    }

    [Fact]
    public async Task ExportIncrementalAsync_should_return_error()
    {
        var result = await _sut.ExportIncrementalAsync("test-service", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.IsType<NotSupportedException>(result.GetException());
    }
}
