using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class InMemoryDcbExecutorWithoutResultTests
{
    private readonly InMemoryDcbExecutorWithoutResult _executor;

    public InMemoryDcbExecutorWithoutResultTests()
    {
        var domainTypes = DcbDomainTypes.Simple(types =>
        {
            // Events & projections reused from the sample domain
            types.EventTypes.RegisterEventType<StudentCreated>();

            types.TagTypes.RegisterTagGroupType<StudentTag>();
            types.TagProjectorTypes.RegisterProjector<StudentProjector>();
            types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();

            types.MultiProjectorTypes
                .RegisterProjectorWithCustomSerialization<
                    GenericTagMultiProjector<StudentProjector, StudentTag>>();

            types.QueryTypes.RegisterQuery<StudentCountWithoutResultQuery>();
            types.QueryTypes.RegisterListQuery<StudentListWithoutResultQuery>();
        });

        _executor = new InMemoryDcbExecutorWithoutResult(domainTypes);
    }

    [Fact]
    public async Task ExecuteAsync_WithSelfHandledCommand_PersistsEventAndState()
    {
        var studentId = Guid.NewGuid();
        var command = new CreateStudentWithoutResult(studentId, "WithoutResult Student", 3);

        var result = await _executor.ExecuteAsync(command);

        Assert.NotNull(result);
        Assert.Equal(1, result.Events.Count());

        var tagStateId = TagStateId.FromProjector<StudentProjector>(new StudentTag(studentId));
        var tagState = await _executor.GetTagStateAsync(tagStateId);
        var studentState = Assert.IsType<StudentState>(tagState.Payload);
        Assert.Equal(studentId, studentState.StudentId);
        Assert.Equal("WithoutResult Student", studentState.Name);
        Assert.Equal(3, studentState.MaxClassCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithHandlerDelegate_ThrowsWhenValidationFails()
    {
        var studentId = Guid.NewGuid();
        var createCommand = new CreateStudentWithoutResult(studentId, "Initial", 2);
        await _executor.ExecuteAsync(createCommand);

        var duplicateCommand = new RegisterStudent(studentId, "Duplicate", 2);
        var exception = await Assert.ThrowsAsync<ApplicationException>(() =>
            _executor.ExecuteAsync(
                duplicateCommand,
                async (cmd, context) =>
                {
                    var tag = new StudentTag(cmd.StudentId);
                    var exists = await context.TagExistsAsync(tag).UnwrapBox();
                    if (exists)
                    {
                        throw new ApplicationException("Student Already Exists");
                    }

                    return EventOrNone.FromValue(
                        new StudentCreated(cmd.StudentId, cmd.Name, cmd.MaxClassCount),
                        tag);
                }));

        Assert.Equal("Student Already Exists", exception.Message);
    }

    [Fact]
    public async Task QueryAsync_UsingWithoutResultInterfaces_ReturnsExpectedData()
    {
        var students = new[]
        {
            new CreateStudentWithoutResult(Guid.NewGuid(), "Charlie", 2),
            new CreateStudentWithoutResult(Guid.NewGuid(), "Alice", 2),
            new CreateStudentWithoutResult(Guid.NewGuid(), "Bob", 2)
        };

        foreach (var student in students)
        {
            await _executor.ExecuteAsync(student);
        }

        var count = await _executor.QueryAsync(new StudentCountWithoutResultQuery());
        Assert.Equal(students.Length, count);

        var listQuery = new StudentListWithoutResultQuery { PageNumber = 1, PageSize = 2 };
        var listResult = await _executor.QueryAsync(listQuery);

        Assert.Equal(students.Length, listResult.TotalCount);
        Assert.Equal(2, listResult.Items.Count());

        var names = listResult.Items.Select(s => s.Name).ToList();
        Assert.Equal(new[] { "Alice", "Bob" }, names);
    }

    private sealed record CreateStudentWithoutResult(Guid StudentId, string Name, int MaxClassCount)
        : ICommandWithHandlerWithoutResult<CreateStudentWithoutResult>
    {
        public static async Task<EventOrNone> HandleAsync(
            CreateStudentWithoutResult command,
            ICommandContext context)
        {
            var tag = new StudentTag(command.StudentId);
            var exists = await context.TagExistsAsync(tag).UnwrapBox();
            if (exists)
            {
                throw new ApplicationException("Student Already Exists");
            }

            return EventOrNone.FromValue(
                new StudentCreated(command.StudentId, command.Name, command.MaxClassCount),
                tag);
        }
    }

    private sealed record RegisterStudent(Guid StudentId, string Name, int MaxClassCount) : ICommand;

    private sealed record StudentCountWithoutResultQuery : IMultiProjectionQueryWithoutResult<
        GenericTagMultiProjector<StudentProjector, StudentTag>,
        StudentCountWithoutResultQuery,
        int>
    {
        public static int HandleQuery(
            GenericTagMultiProjector<StudentProjector, StudentTag> projector,
            StudentCountWithoutResultQuery query,
            IQueryContext context) =>
            projector.GetStatePayloads().OfType<StudentState>().Count();
    }

    private sealed record StudentListWithoutResultQuery : IMultiProjectionListQueryWithoutResult<
        GenericTagMultiProjector<StudentProjector, StudentTag>,
        StudentListWithoutResultQuery,
        StudentState>
    {
        public int? PageNumber { get; init; }
        public int? PageSize { get; init; }

        public static IEnumerable<StudentState> HandleFilter(
            GenericTagMultiProjector<StudentProjector, StudentTag> projector,
            StudentListWithoutResultQuery query,
            IQueryContext context) =>
            projector.GetStatePayloads().OfType<StudentState>();

        public static IEnumerable<StudentState> HandleSort(
            IEnumerable<StudentState> filteredList,
            StudentListWithoutResultQuery query,
            IQueryContext context) =>
            filteredList.OrderBy(s => s.Name, StringComparer.Ordinal);
    }
}
