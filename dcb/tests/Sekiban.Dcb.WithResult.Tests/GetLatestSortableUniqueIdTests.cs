using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb.InMemory;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for ISekibanExecutor.GetLatestSortableUniqueIdAsync (global)
/// </summary>
public class GetLatestSortableUniqueIdTests
{
    [Fact]
    public async Task NoEvents_Should_Return_EmptyString()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        var result = await executor.GetLatestSortableUniqueIdAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.GetValue());
    }

    [Fact]
    public async Task AfterSingleCommand_Should_Return_CommandSortableUniqueId()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        var commandResult = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Test Student", 5));
        Assert.True(commandResult.IsSuccess);

        var commandSortableId = commandResult.GetValue().SortableUniqueId;
        Assert.NotNull(commandSortableId);

        var result = await executor.GetLatestSortableUniqueIdAsync();

        Assert.True(result.IsSuccess);
        var latestId = result.GetValue();
        Assert.NotEmpty(latestId);
        Assert.True(
            string.Compare(latestId, commandSortableId, StringComparison.Ordinal) >= 0,
            $"Expected latestId >= commandSortableId, but got '{latestId}' < '{commandSortableId}'");
    }

    [Fact]
    public async Task AfterMultipleCommands_Should_Return_Latest()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        var result1 = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Student One", 3));
        Assert.True(result1.IsSuccess);
        var id1 = result1.GetValue().SortableUniqueId!;

        var result2 = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Student Two", 4));
        Assert.True(result2.IsSuccess);
        var id2 = result2.GetValue().SortableUniqueId!;

        Assert.True(string.Compare(id2, id1, StringComparison.Ordinal) > 0);

        var result = await executor.GetLatestSortableUniqueIdAsync();

        Assert.True(result.IsSuccess);
        var latestId = result.GetValue();
        Assert.True(
            string.Compare(latestId, id2, StringComparison.Ordinal) >= 0,
            $"Expected latestId >= id2, but got '{latestId}' < '{id2}'");
    }
}
