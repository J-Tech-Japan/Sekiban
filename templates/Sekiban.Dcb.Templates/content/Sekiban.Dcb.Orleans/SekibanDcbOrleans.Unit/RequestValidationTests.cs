using Dcb.Domain;
using Dcb.Domain.ClassRoom;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.Validation;
using SekibanDcbOrleans.ApiService;

namespace SekibanDcbOrleans.Unit;

/// <summary>Request validation: DataAnnotations commands, shared query parameters and projector names.</summary>
public class RequestValidationTests
{
    [TestCase("", 10, "Name")]
    [TestCase("Math 101", 0, "MaxStudents")]
    public async Task CreateClassRoom_InvalidInput_IsRejectedWithFieldErrors(string name, int maxStudents, string field)
    {
        ISekibanExecutor executor = new InMemoryDcbExecutorForTesting(
            DomainType.GetDomainTypes(),
            new InMemoryEventStore(DomainType.GetDomainTypes().EventTypes));
        var created = await executor.ExecuteAsync(
            new CreateClassRoom(Guid.CreateVersion7(), name, maxStudents),
            CreateClassRoomHandler.HandleAsync);
        Assert.That(created.IsSuccess, Is.False);
        var rejection = created.GetException() as SekibanValidationException;
        Assert.That(rejection, Is.Not.Null);
        Assert.That(rejection!.Errors.Select(error => error.PropertyName), Does.Contain(field));
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
