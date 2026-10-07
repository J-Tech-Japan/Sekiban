using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using DcbOrleans.WithoutResult.ApiService.Exceptions;

namespace SekibanDcbOrleansAws.Unit;

public class MalformedRequestTests
{
    [TestCase(true, 400)]
    [TestCase(false, 500)]
    public async Task BadBody_Is400_UnrelatedFailureRemains500(bool badBody, int expected)
    {
        var context = new DefaultHttpContext
        { RequestServices = new ServiceCollection().BuildServiceProvider(), Response = { Body = new MemoryStream() } };
        Exception error = badBody ? new BadHttpRequestException("Malformed JSON") : new Exception("Unrelated");
        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(context, error, default);
        Assert.That(context.Response.StatusCode, Is.EqualTo(expected));
    }
}
