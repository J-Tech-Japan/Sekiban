using Dcb.Domain.WithoutResult;
using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Enrollment;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.WithoutResult.Tests;

/// <summary>
///     The enrollment handlers must reject a student or a classroom that was never created, with a clear message,
///     instead of writing an enrollment event against an empty tag state.
/// </summary>
public class EnrollmentExistenceTests
{
    private readonly ISekibanExecutor _executor = new InMemoryDcbExecutorForTesting(
        DomainType.GetDomainTypes(),
        new InMemoryEventStore(DomainType.GetDomainTypes().EventTypes));

    [Fact]
    public async Task Enroll_InClassRoomThatDoesNotExist_Throws()
    {
        var studentId = Guid.NewGuid();
        await _executor.ExecuteAsync(new CreateStudent(studentId, "Alice", 3));

        var rejection = await Assert.ThrowsAsync<ApplicationException>(() => _executor.ExecuteAsync(
            new EnrollStudentInClassRoom(studentId, Guid.NewGuid()),
            EnrollStudentInClassRoomHandler.HandleAsync));

        Assert.Contains("ClassRoom does not exist", rejection.Message);
    }

    [Fact]
    public async Task Enroll_StudentThatDoesNotExist_Throws()
    {
        var classRoomId = Guid.NewGuid();
        await _executor.ExecuteAsync(new CreateClassRoom(classRoomId, "Math 101"), CreateClassRoomHandler.HandleAsync);

        var rejection = await Assert.ThrowsAsync<ApplicationException>(() => _executor.ExecuteAsync(
            new EnrollStudentInClassRoom(Guid.NewGuid(), classRoomId),
            EnrollStudentInClassRoomHandler.HandleAsync));

        Assert.Contains("Student does not exist", rejection.Message);
    }

    [Fact]
    public async Task Drop_StudentThatDoesNotExist_Throws()
    {
        var classRoomId = Guid.NewGuid();
        await _executor.ExecuteAsync(new CreateClassRoom(classRoomId, "Math 101"), CreateClassRoomHandler.HandleAsync);

        var rejection = await Assert.ThrowsAsync<ApplicationException>(() => _executor.ExecuteAsync(
            new DropStudentFromClassRoom(Guid.NewGuid(), classRoomId),
            DropStudentFromClassRoomHandler.HandleAsync));

        Assert.Contains("Student does not exist", rejection.Message);
    }

    [Fact]
    public async Task Enroll_ExistingStudentAndClassRoom_Succeeds()
    {
        var studentId = Guid.NewGuid();
        var classRoomId = Guid.NewGuid();
        await _executor.ExecuteAsync(new CreateStudent(studentId, "Alice", 3));
        await _executor.ExecuteAsync(new CreateClassRoom(classRoomId, "Math 101"), CreateClassRoomHandler.HandleAsync);

        var result = await _executor.ExecuteAsync(
            new EnrollStudentInClassRoom(studentId, classRoomId),
            EnrollStudentInClassRoomHandler.HandleAsync);

        Assert.NotEmpty(result.Events);
    }
}
