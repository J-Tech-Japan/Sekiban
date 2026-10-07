using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SekibanDcbDeciderAws.ApiService.Exceptions;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.States.Student.Deciders;

namespace SekibanDcbDeciderAws.Unit;

public class DomainRejectionTests
{
    [Test]
    public async Task AlreadyEnrolled_Returns400WithDomainMessage()
    {
        var classroom = Guid.NewGuid();
        var state = new StudentState(Guid.NewGuid(), "Alice", 3, [classroom]);
        var rejection = Assert.Throws<ApplicationException>(() => StudentEnrolledInClassRoomDecider.Validate(state, classroom))!;
        var context = new DefaultHttpContext
        { RequestServices = new ServiceCollection().BuildServiceProvider(), Response = { Body = new MemoryStream() } };
        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(context, rejection, default);
        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        context.Response.Body.Position = 0;
        Assert.That(await new StreamReader(context.Response.Body).ReadToEndAsync(), Does.Contain("already enrolled"));
    }
}
