using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb.InMemory;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for ISekibanExecutor.GetLatestSortableUniqueIdForTagGroupAsync
/// </summary>
public class GetLatestSortableUniqueIdForTagGroupTests
{
    [Fact]
    public async Task EmptyTagGroup_Should_Return_EmptyString()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        var result = await executor.GetLatestSortableUniqueIdForTagGroupAsync("Student");

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.GetValue());
    }

    [Fact]
    public async Task NonExistentTagGroup_Should_Return_EmptyString()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        var result = await executor.GetLatestSortableUniqueIdForTagGroupAsync("NonExistentGroup");

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.GetValue());
    }

    [Fact]
    public async Task AfterSingleCommand_Should_Return_LatestSortableUniqueId()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        var studentId = Guid.NewGuid();
        var commandResult = await executor.ExecuteAsync(new CreateStudent(studentId, "Test Student", 5));
        Assert.True(commandResult.IsSuccess);

        var commandSortableId = commandResult.GetValue().SortableUniqueId;
        Assert.NotNull(commandSortableId);

        var result = await executor.GetLatestSortableUniqueIdForTagGroupAsync("Student");

        Assert.True(result.IsSuccess);
        var latestId = result.GetValue();
        Assert.NotEmpty(latestId);
        // The returned ID should be >= the command's ID (could be equal or later if multiple events)
        Assert.True(
            string.Compare(latestId, commandSortableId, StringComparison.Ordinal) >= 0,
            $"Expected latestId >= commandSortableId, but got '{latestId}' < '{commandSortableId}'");
    }

    [Fact]
    public async Task AfterMultipleCommands_Should_Return_MaxSortableUniqueId()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        var commandResult1 = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Student One", 3));
        Assert.True(commandResult1.IsSuccess);
        var id1 = commandResult1.GetValue().SortableUniqueId!;

        var commandResult2 = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Student Two", 4));
        Assert.True(commandResult2.IsSuccess);
        var id2 = commandResult2.GetValue().SortableUniqueId!;

        // id2 should be later than id1
        Assert.True(string.Compare(id2, id1, StringComparison.Ordinal) > 0);

        var result = await executor.GetLatestSortableUniqueIdForTagGroupAsync("Student");

        Assert.True(result.IsSuccess);
        var latestId = result.GetValue();
        // The returned ID should be >= the second command's ID
        Assert.True(
            string.Compare(latestId, id2, StringComparison.Ordinal) >= 0,
            $"Expected latestId >= id2, but got '{latestId}' < '{id2}'");
    }

    [Fact]
    public async Task GenericOverload_Should_Work()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        var commandResult = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Test", 5));
        Assert.True(commandResult.IsSuccess);

        var result = await executor.GetLatestSortableUniqueIdForTagGroupAsync<StudentTag>();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.GetValue());
    }

    [Fact]
    public async Task InvalidTagGroup_Should_Return_Error()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        // Empty string should fail validation
        var result = await executor.GetLatestSortableUniqueIdForTagGroupAsync("");
        Assert.False(result.IsSuccess);

        // Colon in tag group name should fail validation
        var result2 = await executor.GetLatestSortableUniqueIdForTagGroupAsync("Invalid:Group");
        Assert.False(result2.IsSuccess);
    }
}
