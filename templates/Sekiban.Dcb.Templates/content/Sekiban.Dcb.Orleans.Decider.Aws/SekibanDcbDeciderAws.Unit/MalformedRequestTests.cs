using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SekibanDcbDeciderAws.ApiService.Exceptions;

namespace SekibanDcbDeciderAws.Unit;

public class MalformedRequestTests
{
    [TestCase(400)]
    [TestCase(413)]
    public async Task InvalidBody_PreservesClientErrorStatus(int status)
    {
        var context = new DefaultHttpContext
        { RequestServices = new ServiceCollection().BuildServiceProvider(), Response = { Body = new MemoryStream() } };
        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, new BadHttpRequestException("Malformed JSON", status), default);
        Assert.That(context.Response.StatusCode, Is.EqualTo(status));
    }
}
