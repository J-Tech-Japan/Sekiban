using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.Tests;

public class ServiceIdTests
{
    [Theory]
    [InlineData("default", "default")]
    [InlineData("Service-01", "service-01")]
    [InlineData("A-B-C", "a-b-c")]
    [InlineData("a", "a")]
    public void Normalize_ValidServiceId_ReturnsNormalized(string input, string expected)
    {
        var result = ServiceIdValidator.NormalizeAndValidate(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" bad")]
    [InlineData("bad ")]
    [InlineData("bad|x")]
    [InlineData("bad/x")]
    [InlineData("bad_x")]
    public void Normalize_InvalidServiceId_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => ServiceIdValidator.NormalizeAndValidate(input));
    }

    [Fact]
    public void Normalize_TooLongServiceId_Throws()
    {
        var tooLong = new string('a', 65);
        Assert.Throws<ArgumentException>(() => ServiceIdValidator.NormalizeAndValidate(tooLong));
    }

    [Fact]
    public void FixedServiceIdProvider_ReturnsNormalizedServiceId()
    {
        var provider = new FixedServiceIdProvider("My-Service");
        Assert.Equal("my-service", provider.GetCurrentServiceId());
    }

    [Fact]
    public void RequiredServiceIdProvider_ThrowsWhenAccessed()
    {
        var provider = new RequiredServiceIdProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetCurrentServiceId());
    }
}
