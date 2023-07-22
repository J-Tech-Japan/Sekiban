using Sekiban.Core.Shared;
using Xunit;
namespace Sekiban.Test.CosmosDb;

public class VersioningTest2
{
    [Fact]
    public void Versioning()
    {
        var v1 = new TestV1("Name", "Location");
        var json = SekibanJsonHelper.Serialize(v1);
        var v2 = SekibanJsonHelper.Deserialize<TestV2>(json);
        Assert.Equal("Name", v2?.Name);
        Assert.Equal("Location", v2?.Location);
        Assert.Equal(0, v2?.Point);
    }

    public record TestV1(string Name, string Location);

    public record TestV2(string Name, string Location, int Point);
}
