using Dcb.Domain;
using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using NUnit.Framework;
using Sekiban.Dcb;
using Sekiban.Dcb.Testing;

namespace SekibanDcbOrleans.Unit;

public class SampleTests
{
    [Test]
    public void DomainType_GetDomainTypes_ReturnsNonNull()
    {
        Assert.That(DomainType.GetDomainTypes(), Is.Not.Null);
    }

    [Test]
    public async Task CreateStudent_ThenReadItBack()
    {
        // The unit-test composition, and the only place it belongs: an in-process executor over a volatile store.
        // Both come from the Testing packages, and both say what they are — the executor reports TestingInProcess and
        // the store reports Volatile, so if this composition ever turned up in a Production host with
        // AddSekibanDcbProductionGuard() registered, that host would refuse to start. Which is the intent.
        var domainTypes = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutorForTesting(
            domainTypes,
            new InMemoryEventStore(domainTypes.EventTypes));

        var studentId = Guid.CreateVersion7();

        var executed = await executor.ExecuteAsync(new CreateStudent(studentId, "Alice", 3));
        Assert.That(executed.IsSuccess, Is.True);

        var state = await executor.GetTagStateAsync<StudentProjector>(new StudentTag(studentId));
        Assert.That(state.IsSuccess, Is.True);
        Assert.That(state.GetValue().Payload, Is.TypeOf<StudentState>());
    }

    [Test]
    public async Task EnrollStudent_InClassRoomThatDoesNotExist_IsRejected()
    {
        var domainTypes = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutorForTesting(
            domainTypes,
            new InMemoryEventStore(domainTypes.EventTypes));
        var studentId = Guid.CreateVersion7();
        await executor.ExecuteAsync(new CreateStudent(studentId, "Alice", 3));

        var enrolled = await executor.ExecuteAsync(
            new EnrollStudentInClassRoom(studentId, Guid.CreateVersion7()),
            EnrollStudentInClassRoomHandler.HandleAsync);

        Assert.That(enrolled.IsSuccess, Is.False);
        Assert.That(enrolled.GetException().Message, Does.Contain("ClassRoom does not exist"));
    }

    [Test]
    public async Task EnrollStudent_ThatDoesNotExist_IsRejected()
    {
        var domainTypes = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutorForTesting(
            domainTypes,
            new InMemoryEventStore(domainTypes.EventTypes));
        var classRoomId = Guid.CreateVersion7();
        await executor.ExecuteAsync(new CreateClassRoom(classRoomId, "Math 101"), CreateClassRoomHandler.HandleAsync);

        var enrolled = await executor.ExecuteAsync(
            new EnrollStudentInClassRoom(Guid.CreateVersion7(), classRoomId),
            EnrollStudentInClassRoomHandler.HandleAsync);

        Assert.That(enrolled.IsSuccess, Is.False);
        Assert.That(enrolled.GetException().Message, Does.Contain("Student does not exist"));
    }
}
