using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.ClassRoom;
using DcbOrleans.WithoutResult.ApiService.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.Validation;
using SekibanDcbOrleansAws.ApiService;
using System.Text.Json;

namespace SekibanDcbOrleansAws.Unit;

/// <summary>Request validation: DataAnnotations commands, shared query parameters and projector names.</summary>
public class RequestValidationTests
{
    [TestCase("", 10, "Name")]
    [TestCase("Math 101", 0, "MaxStudents")]
    public async Task CreateClassRoom_InvalidInput_Returns400WithFieldErrors(string name, int maxStudents, string field)
    {
        ISekibanExecutor executor = new InMemoryDcbExecutorForTesting(
            DomainType.GetDomainTypes(),
            new InMemoryEventStore(DomainType.GetDomainTypes().EventTypes));
        var rejection = Assert.ThrowsAsync<SekibanValidationException>(() => executor.ExecuteAsync(
            new CreateClassRoom(Guid.CreateVersion7(), name, maxStudents),
            CreateClassRoomHandler.HandleAsync))!;
        Assert.That(rejection.Errors.Select(error => error.PropertyName), Does.Contain(field));

        var context = new DefaultHttpContext
        { RequestServices = new ServiceCollection().BuildServiceProvider(), Response = { Body = new MemoryStream() } };
        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(context, rejection, default);
        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.That(body.RootElement.GetProperty("errors").TryGetProperty(field, out _), Is.True);
    }

    [TestCase("pageSize", "0")]
    [TestCase("pageSize", "-1")]
    [TestCase("pageNumber", "0")]
    [TestCase("waitForSortableUniqueId", "not-a-sortable-id")]
    public void InvalidQueryParameter_IsRejected(string name, string value)
    {
        var errors = QueryParameterValidationFilter.Validate(
            new QueryCollection(new Dictionary<string, StringValues> { [name] = value }));
        Assert.That(errors.Keys, Is.EquivalentTo(new[] { name }));
    }

    [Test]
    public void ValidQueryParameters_AreAccepted()
    {
        var errors = QueryParameterValidationFilter.Validate(
            new QueryCollection(new Dictionary<string, StringValues>
            {
                ["pageNumber"] = "1",
                ["pageSize"] = "20",
                ["waitForSortableUniqueId"] = SortableUniqueId.GenerateNew()
            }));
        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void ProjectorName_MustBeRegistered()
    {
        var domainTypes = DomainType.GetDomainTypes();
        var registered = domainTypes.MultiProjectorTypes.GetAllProjectorNames();
        Assert.That(registered, Is.Not.Empty);
        Assert.That(KnownProjectorFilter.IsRegistered(domainTypes, registered[0]), Is.True);
        Assert.That(KnownProjectorFilter.IsRegistered(domainTypes, "NoSuchProjector"), Is.False);
    }
}
