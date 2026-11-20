using NUnit.Framework;
using SekibanDcbDeciderAws.ApiService;

namespace SekibanDcbDeciderAws.Unit;

public class LocalS3BucketInitializerTests
{
    [TestCase("http://localhost:4566")]
    [TestCase("http://127.0.0.1:4566")]
    [TestCase("http://[::1]:4566")]
    [TestCase("http://localstack:4566")]
    [TestCase("http://s3.localhost.localstack.cloud.localhost:4566")]
    [TestCase("http://host.docker.internal:4566")]
    public void LocalEmulatorEndpointsAreAllowed(string serviceUrl) =>
        Assert.That(LocalS3BucketInitializer.IsLocalEndpoint(serviceUrl), Is.True);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not a url")]
    [TestCase("https://s3.amazonaws.com")]
    [TestCase("https://s3.us-east-1.amazonaws.com")]
    [TestCase("https://my-bucket.s3.ap-northeast-1.amazonaws.com")]
    [TestCase("http://localhost.example.com:4566")]
    [TestCase("http://10.0.0.5:4566")]
    public void OtherEndpointsAreRejected(string? serviceUrl) =>
        Assert.That(LocalS3BucketInitializer.IsLocalEndpoint(serviceUrl), Is.False);
}
