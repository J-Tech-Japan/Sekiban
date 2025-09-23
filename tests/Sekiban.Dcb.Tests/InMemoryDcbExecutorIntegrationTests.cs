using Dcb.Domain;
using Dcb.Domain.Queries;
using Dcb.Domain.Student;
using Sekiban.Dcb;
using Sekiban.Dcb.InMemory; // InMemoryDcbExecutor
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
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
}
