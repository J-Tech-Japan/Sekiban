using ResultBoxes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Sekiban.Dcb.Orleans.Grains;
using SekibanDcbOrleansAws.ApiService;

namespace SekibanDcbOrleansAws.Unit;

public class ProjectionErrorTests
{
    private static HttpContext Context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() } };
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public async Task RetryableErrors_ResultAndExceptionPathsReturn503(bool catchUp, bool wrapped)
    {
        var prefix = catchUp ? MultiProjectionQueryFailClosedMessages.CatchUpInProgressPrefix
            : MultiProjectionQueryFailClosedMessages.RebuildPendingPrefix;
        var error = new Exception((wrapped ? "Transport wrapper: " : "") + prefix + " details");
        var resultContext = Context();
        var failedResult = ResultBox.Error<int>(error);
        await ProjectionErrors.Map(failedResult.GetException(), Results.BadRequest()).ExecuteAsync(resultContext);
        var exceptionContext = Context();
        Assert.That(await new RetryableProjectionExceptionHandler().TryHandleAsync(exceptionContext, error, default), Is.True);
        foreach (var context in new[] { resultContext, exceptionContext })
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(503));
            Assert.That(context.Response.Headers["Retry-After"].ToString(), Is.EqualTo("2"));
        }
    }

    [Test]
    public async Task UnrelatedError_PreservesExistingStatus()
    {
        var error = new Exception("Unrelated error");
        var context = Context();
        await ProjectionErrors.Map(error, Results.BadRequest()).ExecuteAsync(context);
        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        Assert.That(context.Response.Headers.ContainsKey("Retry-After"), Is.False);
        Assert.That(await new RetryableProjectionExceptionHandler().TryHandleAsync(context, error, default), Is.False);
    }
}
