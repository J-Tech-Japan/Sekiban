using Dcb.Domain;
using Dcb.Domain.Queries;
using Dcb.Domain.Student;
using Sekiban.Dcb;
using Sekiban.Dcb.InMemory; // InMemoryDcbExecutor
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Domains;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Integration style tests verifying that InMemoryDcbExecutor can execute commands and queries end to end.
/// </summary>
public class InMemoryDcbExecutorIntegrationTests
{
	/// <summary>
	///     CreateStudent command should persist events and then GetStudentListQuery returns the created student.
	/// </summary>
	[Fact]
	public async Task CreateStudent_Then_QueryStudentList_Should_Return_Student()
	{
		var domain = DomainType.GetDomainTypes();
		ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

		var studentId = Guid.NewGuid();
		var name = "Integration Test Student";
		var command = new CreateStudent(studentId, name, 5);

		var commandResult = await executor.ExecuteAsync(command);
		Assert.True(commandResult.IsSuccess);

		var listQuery = new GetStudentListQuery
		{
			PageNumber = 1,
			PageSize = 10
		};
		var queryResult = await executor.QueryAsync(listQuery);
		Assert.True(queryResult.IsSuccess);

		var students = queryResult.GetValue().Items.ToList();
		Assert.Contains(students, s => s.StudentId == studentId && s.Name == name);
	}

	[Fact]
	public async Task CreateStudent_Command_Should_Populate_TagState()
	{
		var domain = DomainType.GetDomainTypes();
		ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

		var studentId = Guid.NewGuid();
		var name = "Integration TagState Student";
		var maxClassCount = 4;
		var command = new CreateStudent(studentId, name, maxClassCount);

		var commandResult = await executor.ExecuteAsync(command);
		Assert.True(commandResult.IsSuccess);

		var tagStateId = TagStateId.FromProjector<StudentProjector>(new StudentTag(studentId));
		var tagStateResult = await executor.GetTagStateAsync(tagStateId);
		Assert.True(tagStateResult.IsSuccess);

		var tagState = tagStateResult.GetValue();
		var studentState = Assert.IsType<StudentState>(tagState.Payload);
		Assert.Equal(studentId, studentState.StudentId);
		Assert.Equal(name, studentState.Name);
		Assert.Equal(maxClassCount, studentState.MaxClassCount);
		Assert.Empty(studentState.EnrolledClassRoomIds);
	}

	/// <summary>
	///     Test that InMemoryDcbExecutor should fail when trying to use an unregistered event type.
	///     This test verifies that InMemoryDcbExecutor properly validates event types.
	/// </summary>
	[Fact]
	public async Task UnregisteredEvent_Should_Fail_During_Command_Execution()
	{
		// Create a domain with StudentCreated NOT registered
		var domainTypes = DcbDomainTypes.Simple(types =>
		{
			// Intentionally NOT registering StudentCreated event
			// types.EventTypes.RegisterEventType<StudentCreated>();

			// Register other required types
			types.TagProjectorTypes.RegisterProjector<StudentProjector>();
			types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
			types.TagTypes.RegisterTagGroupType<StudentTag>();
		});

		ISekibanExecutor executor = new InMemoryDcbExecutor(domainTypes);

		var studentId = Guid.NewGuid();
		var command = new CreateStudent(studentId, "Test Student", 5);

		// Execute command - this should fail because StudentCreated is not registered
		var commandResult = await executor.ExecuteAsync(command);

		// After fixing InMemoryDcbExecutor, the command should fail
		Assert.False(commandResult.IsSuccess, "Command should fail when event type is not registered");

		// Verify the error message mentions the event type
		var exception = commandResult.GetException();
		Assert.Contains("StudentCreated", exception.Message);
	}

	[Fact]
	public async Task MultiProjection_Should_Include_AllStudents_AfterMultipleCommands()
	{
		var domain = DomainType.GetDomainTypes();
		ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

		var studentId1 = Guid.NewGuid();
		var studentId2 = Guid.NewGuid();

		await executor.ExecuteAsync(new CreateStudent(studentId1, "Student One", 3));

		// Prime the multi-projection actor
		var initialQuery = await executor.QueryAsync(new GetStudentListQuery { PageNumber = 1, PageSize = 10 });
		Assert.True(initialQuery.IsSuccess);
		Assert.Contains(initialQuery.GetValue().Items, s => s.StudentId == studentId1);

		await executor.ExecuteAsync(new CreateStudent(studentId2, "Student Two", 4));

		var secondQuery = await executor.QueryAsync(new GetStudentListQuery { PageNumber = 1, PageSize = 10 });
		Assert.True(secondQuery.IsSuccess);

		var students = secondQuery.GetValue().Items.ToList();
		Assert.Contains(students, s => s.StudentId == studentId1);
		Assert.Contains(students, s => s.StudentId == studentId2);
	}
}
