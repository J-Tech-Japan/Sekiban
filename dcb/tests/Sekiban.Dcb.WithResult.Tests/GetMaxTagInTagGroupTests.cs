using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.InMemory;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for GetMaxTagInTagGroupAsync functionality.
/// </summary>
public class GetMaxTagInTagGroupTests
{
    [Fact]
    public async Task Empty_TagGroup_Returns_Empty_String()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        var result = await executor.GetMaxTagInTagGroupAsync("Student");
        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.GetValue());
    }

    [Fact]
    public async Task After_Creating_Students_Returns_Max_Tag()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        // Create multiple students with known GUIDs that have clear lexicographic ordering
        var id1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var id2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var id3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

        var r1 = await executor.ExecuteAsync(new CreateStudent(id1, "Alice", 5));
        Assert.True(r1.IsSuccess);
        var r2 = await executor.ExecuteAsync(new CreateStudent(id2, "Bob", 5));
        Assert.True(r2.IsSuccess);
        var r3 = await executor.ExecuteAsync(new CreateStudent(id3, "Charlie", 5));
        Assert.True(r3.IsSuccess);

        var result = await executor.GetMaxTagInTagGroupAsync("Student");
        Assert.True(result.IsSuccess);

        var maxTag = result.GetValue();
        Assert.NotEmpty(maxTag);
        Assert.StartsWith("Student:", maxTag);

        // The max should be the lexicographically largest tag among the three
        var expectedTag1 = $"Student:{id1}";
        var expectedTag2 = $"Student:{id2}";
        var expectedTag3 = $"Student:{id3}";

        // Verify the max tag is one of the created tags
        Assert.Contains(maxTag, new[] { expectedTag1, expectedTag2, expectedTag3 });

        // Verify it's actually the max
        Assert.True(string.Compare(maxTag, expectedTag1, StringComparison.Ordinal) >= 0);
        Assert.True(string.Compare(maxTag, expectedTag2, StringComparison.Ordinal) >= 0);
        Assert.True(string.Compare(maxTag, expectedTag3, StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public async Task NonExistent_TagGroup_Returns_Empty_String()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutor(domain);

        // Create a student so the store isn't empty
        var r = await executor.ExecuteAsync(new CreateStudent(Guid.NewGuid(), "Alice", 5));
        Assert.True(r.IsSuccess);

        // Query a different tag group that doesn't exist
        var result = await executor.GetMaxTagInTagGroupAsync("NonExistentGroup");
        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.GetValue());
    }
}
