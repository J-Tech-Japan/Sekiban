using Dcb.Domain;
using Dcb.Domain.Queries;
using Dcb.Domain.Student;
using Sekiban.Dcb;
using Sekiban.Dcb.InMemory; // InMemoryDcbExecutor
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Queries;
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
		var executor = new InMemoryDcbExecutor(domain);

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
}

